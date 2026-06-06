namespace Wbskt.Workflow.Abstraction.Configuration;

public sealed class WorkflowEngineOptions
{
    public TimeSpan BookmarkPollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan BookmarkOrphanGcInterval { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ScheduleTickInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RunReaperInterval { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan RunStuckThreshold { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan HistoryRetentionInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan HistoryRetentionWindow { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan PendingTriggerEventBacklogInterval { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan PendingTriggerEventTtl { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan MetricsExportInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan IdempotencyRetentionInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan IdempotencyRetentionWindow { get; init; } = TimeSpan.FromHours(24);
    public int LeaseDurationSeconds { get; init; } = 120;
    public int BookmarkLeaseBatchSize { get; init; } = 64;
    public int ScheduledFireLeaseBatchSize { get; init; } = 64;
}
