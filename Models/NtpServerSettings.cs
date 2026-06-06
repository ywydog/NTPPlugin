using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace NtpServer.Models;

public partial class NtpServerSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("port")]
    private int _port = 123;

    [ObservableProperty]
    [property: JsonPropertyName("stratum")]
    private int _stratum = 3;

    [ObservableProperty]
    [property: JsonPropertyName("timeSource")]
    private NtpTimeSource _timeSource = NtpTimeSource.SystemTime;
}

public enum NtpTimeSource
{
    SystemTime,
    ClassIslandTime
}
