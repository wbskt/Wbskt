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

    /// <summary>
    /// How long Warn/Error history entries are kept — the record of what went wrong, so far longer
    /// than routine entries, but not forever. They were previously excluded from collection outright,
    /// which let HistoryEvents grow without bound on a failure-heavy workspace.
    /// </summary>
    public TimeSpan HistoryRetentionWindowElevated { get; init; } = TimeSpan.FromDays(365);
    public TimeSpan PendingTriggerEventBacklogInterval { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan PendingTriggerEventTtl { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan MetricsExportInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan IdempotencyRetentionInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan IdempotencyRetentionWindow { get; init; } = TimeSpan.FromHours(24);
    public decimal DefaultCreditBudgetPerRun { get; init; } = 10000m;
    public int LeaseDurationSeconds { get; init; } = 120;

    /// <summary>Maximum branches this host executes concurrently (BranchExecutionPump's semaphore).</summary>
    public int BranchWorkerLimit { get; init; } = 50;
    public int BookmarkLeaseBatchSize { get; init; } = 64;
    public int ScheduledFireLeaseBatchSize { get; init; } = 64;

    // Engine active/standby leader election (WP4) - separate from LeaseDurationSeconds above,
    // which governs per-item claim leases (bookmarks, scheduled fires), not the engine-leader lease.
    public TimeSpan LeaderElectionRenewInterval { get; init; } = TimeSpan.FromSeconds(10);
    public int LeaderElectionLeaseTtlSeconds { get; init; } = 30;
}
