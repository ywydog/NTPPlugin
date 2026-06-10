using CommunityToolkit.Mvvm.ComponentModel;
using NtpServer.Models;
using NtpServer.Services;

namespace NtpServer.ViewModels;

public partial class NtpServerSettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isRunningAsAdmin;
    [ObservableProperty] private bool _isServiceRunning;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private long _requestCount;
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private List<string> _localIpAddresses = [];
    [ObservableProperty] private bool _isNonStandardPort;
    [ObservableProperty] private List<AddressItem> _classIslandAddresses = [];

    public NtpServerSettings Settings { get; }
    public NtpServerService Service { get; }

    public NtpServerSettingsViewModel(NtpServerSettings settings, NtpServerService service)
    {
        Settings = settings;
        Service = service;
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        IsRunningAsAdmin = AdminHelper.IsRunningInAdmin();
        IsServiceRunning = Service.IsRunning;
        RequestCount = Service.RequestCount;
        LastError = Service.LastError;
        LocalIpAddresses = Service.GetLocalIpAddresses();
        IsNonStandardPort = Settings.Port != 123;

        // 生成 ClassIsland 可直接使用的地址列表
        // ClassIsland 使用 GuerrillaNtp，只接受主机名/IP，标准端口 123 无需指定
        ClassIslandAddresses = LocalIpAddresses
            .Select(ip => new AddressItem(ip))
            .ToList();

        if (IsServiceRunning)
        {
            StatusText = $"NTP 服务正在运行，端口: {Service.Port}";
        }
        else if (!string.IsNullOrEmpty(LastError))
        {
            StatusText = $"NTP 服务启动失败: {LastError}";
        }
        else
        {
            StatusText = "NTP 服务未运行";
        }
    }
}

/// <summary>
/// 用于在 XAML 中绑定显示和复制 ClassIsland 可用地址
/// </summary>
public class AddressItem
{
    /// <summary>
    /// ClassIsland 可直接使用的时间服务器地址（纯 IP，不带端口）
    /// </summary>
    public string Value { get; }

    public AddressItem(string value)
    {
        Value = value;
    }
}
