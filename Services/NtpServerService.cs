using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using NtpServer.Models;

namespace NtpServer.Services;

public class NtpServerService
{
    private readonly ILogger<NtpServerService> _logger;
    private readonly NtpServerSettings _settings;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private long _requestCount;
    private string? _lastError;
    private readonly object _lock = new();

    public NtpServerService(ILogger<NtpServerService> logger, NtpServerSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public bool IsRunning
    {
        get { lock (_lock) return _isRunning; }
    }

    public long RequestCount
    {
        get { lock (_lock) return _requestCount; }
    }

    public string? LastError
    {
        get { lock (_lock) return _lastError; }
    }

    public int Port => _settings.Port;

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                _logger.LogDebug("[NtpServer] 服务已在运行中，跳过启动");
                return;
            }
        }

        _cts = new CancellationTokenSource();

        try
        {
            _udpClient = new UdpClient(_settings.Port);
            _logger.LogInformation("[NtpServer] NTP 服务已启动，监听端口: {Port}", _settings.Port);

            lock (_lock)
            {
                _isRunning = true;
                _lastError = null;
            }

            _ = Task.Run(() => ListenAsync(_cts.Token), _cts.Token);
        }
        catch (SocketException ex)
        {
            _lastError = $"端口 {_settings.Port} 绑定失败: {ex.Message}";
            _logger.LogError(ex, "[NtpServer] 无法绑定端口 {Port}: {Message}", _settings.Port, ex.Message);
        }
        catch (Exception ex)
        {
            _lastError = $"服务启动失败: {ex.Message}";
            _logger.LogError(ex, "[NtpServer] 服务启动失败: {Message}", ex.Message);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                _logger.LogDebug("[NtpServer] 服务未在运行，跳过停止");
                return;
            }
            _isRunning = false;
        }

        try
        {
            _cts?.Cancel();
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;
            _logger.LogInformation("[NtpServer] NTP 服务已停止");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NtpServer] 停止服务时发生异常: {Message}", ex.Message);
        }
    }

    public void Restart()
    {
        _logger.LogInformation("[NtpServer] 正在重启 NTP 服务...");
        Stop();
        Start();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_udpClient == null) break;

                var result = await _udpClient.ReceiveAsync(cancellationToken);
                _ = Task.Run(() => HandleRequest(result), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NtpServer] 接收数据时发生错误: {Message}", ex.Message);
            }
        }
    }

    private void HandleRequest(UdpReceiveResult result)
    {
        try
        {
            var request = result.Buffer;
            if (request.Length < 48)
            {
                _logger.LogDebug("[NtpServer] 收到无效的 NTP 请求包（长度 {Length}）", request.Length);
                return;
            }

            var response = BuildNtpResponse(request);
            _udpClient?.SendAsync(response, response.Length, result.RemoteEndPoint);

            Interlocked.Increment(ref _requestCount);
            _logger.LogDebug("[NtpServer] 已响应来自 {RemoteEndPoint} 的 NTP 请求", result.RemoteEndPoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NtpServer] 处理请求时发生错误: {Message}", ex.Message);
        }
    }

    private byte[] BuildNtpResponse(byte[] request)
    {
        var response = new byte[48];

        // Byte 0: LI(00) + VN(011) + Mode(100) = 0x1C
        response[0] = 0x1C;

        // Byte 1: Stratum
        response[1] = (byte)_settings.Stratum;

        // Byte 2: Poll interval
        response[2] = 6;

        // Byte 3: Precision (-6)
        response[3] = 0xFA;

        // Byte 4-7: Root Delay (0)
        // Byte 8-11: Root Dispersion (0)
        // Byte 12-15: Reference ID ("LOCL")
        response[12] = (byte)'L';
        response[13] = (byte)'O';
        response[14] = (byte)'C';
        response[15] = (byte)'L';

        var currentTime = GetCurrentTime();
        var ntpTime = DateTimeToNtpTimestamp(currentTime);

        // Byte 16-23: Reference Timestamp
        WriteNtpTimestamp(response, 16, ntpTime);

        // Byte 24-31: Originate Timestamp (copy from request)
        Buffer.BlockCopy(request, 24, response, 24, 8);

        // Byte 32-39: Receive Timestamp
        WriteNtpTimestamp(response, 32, ntpTime);

        // Byte 40-47: Transmit Timestamp
        WriteNtpTimestamp(response, 40, ntpTime);

        return response;
    }

    private DateTime GetCurrentTime()
    {
        if (_settings.TimeSource == NtpTimeSource.ClassIslandTime)
        {
            try
            {
                var exactTimeService = IAppHost.TryGetService<ClassIsland.Core.Abstractions.Services.IExactTimeService>();
                if (exactTimeService != null)
                {
                    var localTime = exactTimeService.GetCurrentLocalDateTime();
                    _logger.LogDebug("[NtpServer] 使用 ClassIsland 精确时间: {Time}", localTime);
                    return localTime.ToUniversalTime();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[NtpServer] 获取 ClassIsland 精确时间失败，回退到系统时间");
            }
        }

        return DateTime.UtcNow;
    }

    private static ulong DateTimeToNtpTimestamp(DateTime utcTime)
    {
        var ntpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var elapsed = utcTime - ntpEpoch;
        var seconds = (uint)elapsed.TotalSeconds;
        var fraction = (uint)((elapsed.TotalSeconds - seconds) * 4294967296.0);
        return ((ulong)seconds << 32) | fraction;
    }

    private static void WriteNtpTimestamp(byte[] buffer, int offset, ulong timestamp)
    {
        buffer[offset] = (byte)(timestamp >> 56);
        buffer[offset + 1] = (byte)(timestamp >> 48);
        buffer[offset + 2] = (byte)(timestamp >> 40);
        buffer[offset + 3] = (byte)(timestamp >> 32);
        buffer[offset + 4] = (byte)(timestamp >> 24);
        buffer[offset + 5] = (byte)(timestamp >> 16);
        buffer[offset + 6] = (byte)(timestamp >> 8);
        buffer[offset + 7] = (byte)timestamp;
    }

    public List<string> GetLocalIpAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                             && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork
                               && !IPAddress.IsLoopback(addr.Address))
                .Select(addr => addr.Address.ToString())
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NtpServer] 获取本机 IP 地址失败: {Message}", ex.Message);
            return [];
        }
    }
}
