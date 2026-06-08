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
