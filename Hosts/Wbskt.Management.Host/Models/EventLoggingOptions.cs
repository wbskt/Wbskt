namespace Wbskt.Management.Host.Models;

public sealed class EventLoggingOptions
{
    public const string SectionName = "EventLogging";

    public int MaxBufferSize { get; set; } = 10000;
    public int BatchSize { get; set; } = 100;
    public int FlushIntervalLimitInSeconds { get; set; } = 5;
}
