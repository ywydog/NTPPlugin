# NTP 时间同步服务端插件设计文档

## 概述

为 ClassIsland 开发一个 NTP 时间同步服务端插件，在本机启动 NTP Server，为局域网内其他设备提供时间同步服务。插件**加载后自动启动 NTP 服务**，支持可配置端口（默认 UDP 123）、双时间源（系统时间 / ClassIsland 精确时间），在设置页面检测管理员权限并显示"以管理员身份重启"按钮，同时**显示本机 IP 地址和连接信息**，方便其他 ClassIsland 端配置时间同步。

## 当前状态分析

### 参考项目

1. **ClassIsland 插件框架**（已探索）
   - 插件基类：`PluginBase`，入口特性：`[PluginEntrance]`
   - 设置页面基类：`SettingsPageBase`，特性：`[SettingsPageInfo]`
   - DI 注册扩展：`services.AddSettingsPage<T>()`
   - 生命周期事件：`AppBase.Current.AppStarted` / `AppStopping`
   - 内部时间服务：`IExactTimeService.GetCurrentLocalDateTime()`

2. **StartUpAsAdmin**（`/workspace/StartUpAsAdmin`，已克隆）
   - `AdminHelper.IsRunningInAdmin()` — 使用 `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)` 检测管理员权限
   - 设置页面使用 `InfoBar` 控件显示权限警告，`Button` 触发 `ProcessStartInfo.Verb = "runas"` 重启
   - ViewModel 使用 `CommunityToolkit.Mvvm.ComponentModel.ObservableObject` + `[ObservableProperty]`

3. **SystemTools**（`/workspace/SystemTools`，已探索）
   - 完整的 ClassIsland 插件实例，展示了 Action/Trigger/Component/SettingsPage 的注册和使用模式

### 关键约束

- NTP 标准端口 UDP 123 需要**管理员权限**才能绑定
- 非特权端口（>1024）无需管理员权限，但客户端需特殊配置
- Windows 平台专属功能（管理员权限检测）
- ClassIsland 2.x 插件 API（`apiVersion: 2.0.0.1`）

## 设计方案

### 方案选择：自研 NTP 协议实现（方案 A）

选择理由：
- 零外部依赖，插件体积小
- 完全控制 NTP 协议细节（Stratum、精度、时间源）
- 可灵活支持 ClassIsland 内部时间源
- SNTP 协议核心部分简单，约 200-300 行代码

### 插件基本信息

| 字段 | 值 |
|------|------|
| ID | `classisland.ntpServer` |
| 名称 | NTP 时间同步服务端 |
| 描述 | 在本机启动 NTP 服务端，为局域网设备提供时间同步服务 |
| 入口程序集 | `NtpServer.dll` |
| API 版本 | `2.0.0.1` |
| 目标框架 | `net8.0-windows` |

### 文件结构

```
NtpServer/
├── manifest.yml                          # 插件清单
├── icon.png                              # 插件图标
├── NtpServer.csproj                      # 项目配置
├── Plugin.cs                             # 插件入口
├── AdminHelper.cs                        # 管理员权限检测（参照 StartUpAsAdmin）
├── Services/
│   └── NtpServerService.cs               # NTP 服务端核心
├── Models/
│   └── NtpServerSettings.cs              # 设置数据模型
├── ViewModels/
│   └── NtpServerSettingsViewModel.cs     # 设置页面 ViewModel
├── NtpServerSettingsPage.axaml           # 设置页面 UI
└── NtpServerSettingsPage.axaml.cs        # 设置页面代码
```

### 核心组件设计

#### 1. Plugin.cs — 插件入口

**关键行为：插件加载后自动启动 NTP 服务**，无需用户手动启用。

```csharp
[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 注册设置页面
        services.AddSettingsPage<NtpServerSettingsPage>();

        // 注册 NTP 服务为单例
        services.AddSingleton<NtpServerService>();

        // 订阅应用启动事件，自动启动 NTP 服务
        AppBase.Current.AppStarted += (s, e) =>
        {
            var ntpService = IAppHost.GetService<NtpServerService>();
            ntpService.Start();  // 自动启动，根据配置决定端口
        };

        // 订阅应用停止事件，停止 NTP 服务
        AppBase.Current.AppStopping += (s, e) =>
        {
            var ntpService = IAppHost.TryGetService<NtpServerService>();
            ntpService?.Stop();
        };
    }
}
```

#### 2. AdminHelper.cs — 管理员权限检测

直接参照 StartUpAsAdmin 的实现：

```csharp
using System.Security.Principal;

public static class AdminHelper
{
    public static bool IsRunningInAdmin()
    {
        var id = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(id);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
```

#### 3. NtpServerService.cs — NTP 服务端核心

**职责**：
- 在指定端口监听 UDP NTP 请求
- 根据配置的时间源返回 NTP 响应
- 管理服务生命周期（Start/Stop）
- **提供本机 IP 地址列表**，供设置页面显示连接信息
- **统计连接数**（已响应的 NTP 请求数）

**NTP 协议实现**（SNTP v4，RFC 4330）：
- NTP 包格式：48 字节
  - Byte 0: LI(2bit) + VN(3bit, =3) + Mode(3bit, =4 Server)
  - Byte 1: Stratum（默认 3）
  - Byte 2: Poll（默认 6 = 64秒）
  - Byte 3: Precision（默认 -6）
  - Byte 4-7: Root Delay（0）
  - Byte 8-11: Root Dispersion（0）
  - Byte 12-15: Reference ID（"LOCL"）
  - Byte 16-23: Reference Timestamp
  - Byte 24-31: Originate Timestamp（从请求包复制）
  - Byte 32-39: Receive Timestamp
  - Byte 40-47: Transmit Timestamp

**时间戳转换**：
- NTP 时间戳 = 从 1900-01-01 00:00:00 UTC 的秒数（高 32 位整数秒 + 低 32 位小数秒）
- 转换：`(dateTime - new DateTime(1900, 1, 1)).TotalSeconds` → 拆分整数和小数部分

**时间源**：
- 系统时间：`DateTime.UtcNow`
- ClassIsland 精确时间：`IAppHost.TryGetService<IExactTimeService>()?.GetCurrentLocalDateTime().ToUniversalTime() ?? DateTime.UtcNow`

**端口绑定策略**：
- 默认尝试绑定 UDP 123
- 如果端口被占用或权限不足，记录错误日志，服务标记为启动失败
- 设置页面可配置端口，改为非特权端口（如 1123）后无需管理员权限
- 启动失败时设置页面显示错误信息

**连接信息**：
- `GetLocalIpAddresses()` — 获取本机所有局域网 IPv4 地址（过滤掉回环和虚拟网卡）
- `RequestCount` — 已响应的 NTP 请求计数
- `IsRunning` — 服务运行状态
- `LastError` — 最近一次错误信息

**关键方法**：
```csharp
public class NtpServerService
{
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private long _requestCount;
    private string? _lastError;

    public void Start();                              // 绑定端口，开始监听
    public void Stop();                               // 停止监听，释放资源
    public void Restart();                            // 重启服务（设置变更后调用）
    private void ListenAsync();                       // 异步接收循环
    private byte[] BuildNtpResponse(byte[] request);  // 构建 NTP 响应包
    private DateTime GetCurrentTime();                // 根据配置获取当前时间
    public List<string> GetLocalIpAddresses();        // 获取本机局域网 IP 地址
    public bool IsRunning { get; }
    public long RequestCount { get; }
    public string? LastError { get; }
}
```

**获取本机 IP 地址实现**：
```csharp
public List<string> GetLocalIpAddresses()
{
    return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                     && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
        .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork  // IPv4
                       && !IPAddress.IsLoopback(addr.Address))
        .Select(addr => addr.Address.ToString())
        .ToList();
}
```

#### 4. NtpServerSettings.cs — 设置数据模型

```csharp
public partial class NtpServerSettings : ObservableObject
{
    [ObservableProperty] private int _port = 123;                  // 监听端口
    [ObservableProperty] private int _stratum = 3;                 // Stratum 级别
    [ObservableProperty] private NtpTimeSource _timeSource = NtpTimeSource.SystemTime; // 时间源
}

public enum NtpTimeSource
{
    SystemTime,        // 系统时间
    ClassIslandTime    // ClassIsland 精确时间
}
```

**注意**：移除了 `IsEnabled` 字段，因为插件加载后自动启动服务，无需手动启用/禁用。

设置持久化：使用 JSON 文件存储在 `PluginConfigFolder` 目录下。

#### 5. NtpServerSettingsViewModel.cs — 设置页面 ViewModel

```csharp
public partial class NtpServerSettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isRunningAsAdmin = false;       // 是否管理员
    [ObservableProperty] private bool _isServiceRunning = false;       // 服务是否运行中
    [ObservableProperty] private string _statusText = "";              // 状态文本
    [ObservableProperty] private string _connectionInfo = "";          // 连接信息（IP:端口）
    [ObservableProperty] private long _requestCount = 0;              // 已响应请求数
    [ObservableProperty] private string? _lastError;                  // 最近错误
    [ObservableProperty] private NtpServerSettings _settings;          // 设置引用
    [ObservableProperty] private List<string> _localIpAddresses = [];  // 本机 IP 地址列表
}
```

#### 6. NtpServerSettingsPage — 设置页面 UI

**布局**（参照 StartUpAsAdmin 的 `SettingsPageBase` + `InfoBar` 模式）：

```
SettingsPageBase
└── ScrollViewer
    └── StackPanel (settings-container animated-intro)
        │
        ├── InfoBar (Severity=Error, 非管理员且端口<1024时显示)
        │   ├── Message: "你需要以管理员身份运行 ClassIsland 才能使用标准 NTP 端口(123)。"
        │   └── ActionButton: "以管理员身份重启"
        │
        ├── InfoBar (Severity=Error, 服务启动失败时显示)
        │   └── Message: "NTP 服务启动失败: {LastError}"
        │
        ├── InfoBar (Severity=Success, 服务运行中时显示)
        │   └── Message: "NTP 服务正在运行"
        │
        ├── SettingsExpander: "连接信息" (IsExpanded=True, 服务运行时)
        │   ├── Description: "在其他 ClassIsland 端使用以下地址同步时间"
        │   ├── SettingsExpanderItem: 本机 IP 列表（每个 IP 显示为 "IP:端口" 格式）
        │   │   └── 每项带复制按钮，点击复制到剪贴板
        │   └── SettingsExpanderItem: "已响应请求数: {RequestCount}"
        │
        ├── SettingsExpander: "NTP 服务设置" (IsExpanded=True)
        │   ├── SettingsExpanderItem: 监听端口 (NumberBox, 1-65535)
        │   ├── SettingsExpanderItem: 时间源 (ComboBox: 系统时间/ClassIsland精确时间)
        │   └── SettingsExpanderItem: Stratum 级别 (NumberBox, 1-15)
        │
        └── SettingsExpander: "服务控制"
            ├── SettingsExpanderItem: "重启服务" (按钮，修改设置后需重启生效)
            └── SettingsExpanderItem: "停止服务" (按钮)
```

**连接信息显示逻辑**：
- 服务运行时，自动获取本机所有局域网 IPv4 地址
- 每个地址显示为 `{IP}:{Port}` 格式（如 `192.168.1.100:123`）
- 每项旁边有复制按钮，点击后将地址复制到剪贴板
- 方便用户在其他 ClassIsland 端的时间同步设置中粘贴使用

**管理员重启逻辑**（参照 StartUpAsAdmin）：
```csharp
private void ButtonRestartAsAdmin_OnClick(object sender, RoutedEventArgs e)
{
    var processStartInfo = new ProcessStartInfo()
    {
        FileName = Environment.ProcessPath?.Replace(".dll", ".exe"),
        ArgumentList = { "-m", "--uri", "classisland://app/settings/classisland.ntpServer" },
        Verb = "runas",
        UseShellExecute = true
    };
    var args = Environment.GetCommandLineArgs().ToList();
    args.RemoveAt(0);
    foreach (var i in args) processStartInfo.ArgumentList.Add(i);
    Process.Start(processStartInfo);
    AppBase.Current.Stop();
}
```

### 项目配置 (NtpServer.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net8.0-windows</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <EnableDynamicLoading>True</EnableDynamicLoading>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="ClassIsland.PluginSdk" Version="2.0.0.1">
            <ExcludeAssets>runtime; native</ExcludeAssets>
        </PackageReference>
    </ItemGroup>
    <ItemGroup>
        <None Update="manifest.yml"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
        <None Update="icon.png"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
        <None Update="README.md"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
    </ItemGroup>
</Project>
```

### 插件清单 (manifest.yml)

```yaml
id: classisland.ntpServer
name: NTP 时间同步服务端
description: 在本机启动 NTP 服务端，为局域网设备提供时间同步服务
entranceAssembly: "NtpServer.dll"
url: https://github.com/ClassIsland
version: 1.0.0.0
apiVersion: 2.0.0.1
author: ClassIsland
icon: icon.png
```

## 假设与决策

| 决策 | 选择 | 理由 |
|------|------|------|
| NTP 协议实现方式 | 自研 SNTP | 零依赖，灵活支持自定义时间源 |
| 端口策略 | 可配置，默认 123 | 兼顾标准用法和权限限制 |
| 时间源 | 系统时间 + ClassIsland 精确时间 | 用户明确要求支持两种时间源 |
| 管理员权限处理 | 参照 StartUpAsAdmin | 成熟的检测和重启模式 |
| 设置持久化 | JSON 文件 | 简单直接，与 ClassIsland 插件惯例一致 |
| Stratum 默认值 | 3 | 适合本地网络级别的时间服务器 |
| 目标平台 | Windows only | 管理员权限检测为 Windows 专属 |
| 自启动策略 | 插件加载后自动启动 | 用户要求无需手动启用 |
| 连接信息展示 | 显示本机 IP + 端口 + 复制按钮 | 方便其他 ClassIsland 端配置时间同步 |

## 验证步骤

1. **构建验证**：`dotnet build` 成功
2. **自启动验证**：插件加载后 NTP 服务自动启动，无需手动操作
3. **管理员权限检测**：非管理员运行时，设置页面显示红色 InfoBar 和重启按钮
4. **NTP 服务启动**：管理员模式下自动绑定 UDP 123 成功
5. **非特权端口**：非管理员模式下，改为端口 1123 后服务可正常启动
6. **NTP 响应正确性**：使用 `w32tm /stripchart /computer:localhost /dataonly` 或 Python `ntplib` 验证时间同步
7. **ClassIsland 时间源**：选择 ClassIsland 精确时间后，NTP 返回的时间与 ClassIsland 内部时间一致
8. **连接信息显示**：设置页面正确显示本机局域网 IP 地址和端口，复制按钮可用
9. **服务生命周期**：应用启动时自动启动服务，应用关闭时服务正确停止
10. **设置持久化**：修改设置后重启应用，设置保留且服务使用新配置启动
