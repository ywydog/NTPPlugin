# NTP 时间同步服务端插件设计文档

## 概述

为 ClassIsland 开发一个 NTP 时间同步服务端插件，在本机启动 NTP Server，为局域网内其他设备提供时间同步服务。插件支持可配置端口（默认 UDP 123）、双时间源（系统时间 / ClassIsland 精确时间），并在设置页面检测管理员权限，非管理员时显示警告和"以管理员身份重启"按钮。

## 当前状态分析

### 参考项目

1. **ClassIsland 插件框架**（`/workspace/ClassIsland`，已探索）
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
- ClassIsland 2.x 插件 API（`apiVersion: 2.0.0.0`）

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
| API 版本 | `2.0.0.0` |
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

        // 订阅应用启动事件，启动 NTP 服务
        AppBase.Current.AppStarted += (s, e) =>
        {
            var ntpService = IAppHost.GetService<NtpServerService>();
            ntpService.Start();
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
- 如果端口被占用或权限不足，记录错误日志
- 设置页面可配置端口，改为非特权端口（如 1123）后无需管理员权限

**关键方法**：
```csharp
public class NtpServerService
{
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public void Start();    // 绑定端口，开始监听
    public void Stop();     // 停止监听，释放资源
    private void ListenAsync();  // 异步接收循环
    private byte[] BuildNtpResponse(byte[] request); // 构建 NTP 响应包
    private DateTime GetCurrentTime(); // 根据配置获取当前时间
}
```

#### 4. NtpServerSettings.cs — 设置数据模型

```csharp
public partial class NtpServerSettings : ObservableObject
{
    [ObservableProperty] private bool _isEnabled = false;           // 是否启用 NTP 服务
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

设置持久化：使用 JSON 文件存储在 `PluginConfigFolder` 目录下。

#### 5. NtpServerSettingsViewModel.cs — 设置页面 ViewModel

```csharp
public partial class NtpServerSettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isRunningAsAdmin = false;    // 是否管理员
    [ObservableProperty] private bool _isServiceRunning = false;    // 服务是否运行中
    [ObservableProperty] private string _statusText = "";           // 状态文本
    [ObservableProperty] private NtpServerSettings _settings;       // 设置引用
}
```

#### 6. NtpServerSettingsPage — 设置页面 UI

**布局**（参照 StartUpAsAdmin 的 `SettingsPageBase` + `InfoBar` 模式）：

```
SettingsPageBase
└── ScrollViewer
    └── StackPanel (settings-container animated-intro)
        ├── InfoBar (Severity=Error, 非管理员时显示)
        │   ├── Message: "你需要以管理员身份运行 ClassIsland 才能使用标准 NTP 端口(123)。"
        │   └── ActionButton: "以管理员身份重启"
        │
        ├── InfoBar (Severity=Informational, 服务运行中时显示)
        │   └── Message: "NTP 服务正在运行，端口: 123"
        │
        ├── SettingsExpander: "NTP 服务设置"
        │   ├── ToggleSwitch: 启用/禁用 NTP 服务
        │   ├── SettingsExpanderItem: 监听端口 (NumberBox, 1-65535)
        │   ├── SettingsExpanderItem: 时间源 (ComboBox: 系统时间/ClassIsland精确时间)
        │   └── SettingsExpanderItem: Stratum 级别 (NumberBox, 1-15)
        │
        └── SettingsExpander: "服务控制"
            ├── SettingsExpanderItem: "启动服务" (按钮)
            └── SettingsExpanderItem: "停止服务" (按钮)
```

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
apiVersion: 2.0.0.0
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

## 验证步骤

1. **构建验证**：`dotnet build` 成功
2. **管理员权限检测**：非管理员运行时，设置页面显示红色 InfoBar 和重启按钮
3. **NTP 服务启动**：启用后，在管理员模式下绑定 UDP 123 成功
4. **非特权端口**：非管理员模式下，改为端口 1123 后服务可正常启动
5. **NTP 响应正确性**：使用 `w32tm /stripchart /computer:localhost /dataonly` 或 Python `ntplib` 验证时间同步
6. **ClassIsland 时间源**：选择 ClassIsland 精确时间后，NTP 返回的时间与 ClassIsland 内部时间一致
7. **服务生命周期**：应用启动时自动启动服务，应用关闭时服务正确停止
8. **设置持久化**：修改设置后重启应用，设置保留
