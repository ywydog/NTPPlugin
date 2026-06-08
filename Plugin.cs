using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NtpServer.Models;
using NtpServer.Services;

namespace NtpServer;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 加载设置（不依赖 logger）
        var settings = LoadSettings();

        // 注册设置页面
        services.AddSettingsPage<NtpServerSettingsPage>();

        // 注册 NTP 服务为单例
        services.AddSingleton<NtpServerService>(sp =>
        {
            var serviceLogger = sp.GetRequiredService<ILogger<NtpServerService>>();
            return new NtpServerService(serviceLogger, settings);
        });

        // 订阅应用启动事件，自动启动 NTP 服务
        AppBase.Current.AppStarted += (s, e) =>
        {
            var logger = IAppHost.TryGetService<ILogger<Plugin>>();
            logger?.LogInformation("[NtpServer] 应用已启动，正在启动 NTP 服务...");
            try
            {
                var ntpService = IAppHost.GetService<NtpServerService>();
                ntpService.Start();
                logger?.LogInformation("[NtpServer] NTP 服务自动启动完成");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[NtpServer] 自动启动 NTP 服务失败: {Message}", ex.Message);
            }
        };

        // 订阅应用停止事件，停止 NTP 服务
        AppBase.Current.AppStopping += (s, e) =>
        {
            var logger = IAppHost.TryGetService<ILogger<Plugin>>();
            logger?.LogInformation("[NtpServer] 应用正在停止，正在停止 NTP 服务...");
            try
            {
                var ntpService = IAppHost.TryGetService<NtpServerService>();
                ntpService?.Stop();
                logger?.LogInformation("[NtpServer] NTP 服务已停止");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[NtpServer] 停止 NTP 服务时发生异常: {Message}", ex.Message);
            }
        };
    }

    private static NtpServerSettings LoadSettings()
    {
        try
        {
            var pluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "NtpServer");
            var configPath = Path.Combine(pluginDir, "NtpServerSettings.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<NtpServerSettings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // 忽略加载错误，使用默认设置
        }

        return new NtpServerSettings();
    }
}
