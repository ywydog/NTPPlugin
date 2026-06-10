using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Helpers.UI;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using NtpServer.Models;
using NtpServer.Services;
using NtpServer.ViewModels;

namespace NtpServer;

[SettingsPageInfo("classisland.ntpServer", "NTP 时间同步服务端", "\ue770", "\ue771")]
public partial class NtpServerSettingsPage : SettingsPageBase
{
    private readonly ILogger<NtpServerSettingsPage>? _logger;
    private readonly DispatcherTimer _refreshTimer;

    public NtpServerSettingsViewModel ViewModel { get; }

    public NtpServerSettingsPage()
    {
        var settings = LoadSettings();
        var service = IAppHost.TryGetService<NtpServerService>();

        if (service == null)
        {
            // 服务尚未注册，创建临时实例（仅在设计时）
            var logger = IAppHost.TryGetService<ILogger<NtpServerService>>();
            service = new NtpServerService(logger!, settings);
        }

        ViewModel = new NtpServerSettingsViewModel(settings, service);
        DataContext = this;
        InitializeComponent();

        _logger = IAppHost.TryGetService<ILogger<NtpServerSettingsPage>>();

        // 定时刷新状态
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => ViewModel.RefreshStatus();
        _refreshTimer.Start();

        _logger?.LogInformation("[NtpServer] 设置页面已加载");
    }

    private static NtpServerSettings LoadSettings()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "NtpServer", "NtpServerSettings.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var settings = JsonSerializer.Deserialize<NtpServerSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch (Exception)
        {
            // 忽略加载错误，使用默认设置
        }
        return new NtpServerSettings();
    }

    private void SaveSettings()
    {
        try
        {
            var pluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "NtpServer");
            Directory.CreateDirectory(pluginDir);
            var configPath = Path.Combine(pluginDir, "NtpServerSettings.json");
            var json = JsonSerializer.Serialize(ViewModel.Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
            _logger?.LogInformation("[NtpServer] 设置已保存到 {Path}", configPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[NtpServer] 保存设置失败: {Message}", ex.Message);
            this.ShowErrorToast("保存设置失败", ex);
        }
    }

    private void ButtonRestartAsAdmin_OnClick(object? sender, RoutedEventArgs e)
    {
        try
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
            foreach (var arg in args)
            {
                processStartInfo.ArgumentList.Add(arg);
            }
            Process.Start(processStartInfo);
            AppBase.Current.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[NtpServer] 以管理员身份重启失败: {Message}", ex.Message);
            this.ShowErrorToast("无法以管理员身份重启", ex);
        }
    }

    private void ButtonRestartService_OnClick(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        ViewModel.Service.Restart();
        ViewModel.RefreshStatus();
        this.ShowSuccessToast("NTP 服务已重启");
        _logger?.LogInformation("[NtpServer] 用户手动重启了 NTP 服务，端口: {Port}", ViewModel.Settings.Port);
    }

    private void ButtonStopService_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.Service.Stop();
        ViewModel.RefreshStatus();
        this.ShowSuccessToast("NTP 服务已停止");
        _logger?.LogInformation("[NtpServer] 用户手动停止了 NTP 服务");
    }

    private async void ButtonCopyAddress_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string address)
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(address);
                    this.ShowSuccessToast($"已复制: {address}");
                    _logger?.LogDebug("[NtpServer] 用户复制了地址: {Address}", address);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[NtpServer] 复制到剪贴板失败: {Message}", ex.Message);
            }
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _refreshTimer.Stop();
    }
}
