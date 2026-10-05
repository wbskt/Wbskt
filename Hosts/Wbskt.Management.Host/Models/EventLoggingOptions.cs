namespace Wbskt.Management.Host.Models;

public sealed class EventLoggingOptions
{
    public const string SectionName = "EventLogging";

    /// <summary>The most events written in one insert.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>The longest an event waits for its batch to fill before the batch is written anyway.</summary>
    public int FlushIntervalLimitInSeconds { get; set; } = 5;

    /// <summary>How long an event log entry is kept before the retention sweep deletes it.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>How often the retention sweep runs.</summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);
}
