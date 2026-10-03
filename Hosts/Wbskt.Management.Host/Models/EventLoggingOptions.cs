namespace Wbskt.Management.Host.Models;

public sealed class EventLoggingOptions
{
    public const string SectionName = "EventLogging";

    public int MaxBufferSize { get; set; } = 10000;
    public int BatchSize { get; set; } = 100;
    public int FlushIntervalLimitInSeconds { get; set; } = 5;

    /// <summary>How long an event log entry is kept before the retention sweep deletes it.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>How often the retention sweep runs.</summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);
}
