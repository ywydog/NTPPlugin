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
        var loggerFactory = services.BuildServiceProvider().GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger<Plugin>();

        logger?.LogInformation("[NtpServer] 插件正在初始化...");

        // 加载设置
        var settings = LoadSettings(logger);

        // 注册设置页面
        services.AddSettingsPage<NtpServerSettingsPage>();
        logger?.LogInformation("[NtpServer] 已注册设置页面");

        // 注册 NTP 服务为单例
        services.AddSingleton<NtpServerService>(sp =>
        {
            var serviceLogger = sp.GetRequiredService<ILogger<NtpServerService>>();
            return new NtpServerService(serviceLogger, settings);
        });
        logger?.LogInformation("[NtpServer] 已注册 NTP 服务");

        // 订阅应用启动事件，自动启动 NTP 服务
        AppBase.Current.AppStarted += (s, e) =>
        {
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

        logger?.LogInformation("[NtpServer] 插件初始化完成");
    }

    private static NtpServerSettings LoadSettings(ILogger? logger)
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
                    logger?.LogInformation("[NtpServer] 已从 {Path} 加载设置", configPath);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[NtpServer] 加载设置失败，使用默认设置: {Message}", ex.Message);
        }

        logger?.LogInformation("[NtpServer] 使用默认设置");
        return new NtpServerSettings();
    }
}
