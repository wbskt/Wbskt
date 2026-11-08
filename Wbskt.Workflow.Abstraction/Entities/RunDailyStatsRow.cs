namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>One workflow's runs started on one UTC day (<c>Run_GetDailyStats*</c>).</summary>
public sealed record RunDailyStatsRow
{
    public required Guid WorkflowRefId { get; init; }
    public required DateOnly Day { get; init; }
    public required int TotalRuns { get; init; }
    public required int SucceededCount { get; init; }

    /// <summary>Every way of not succeeding: failed, partially failed, out of credits.</summary>
    public required int FailedCount { get; init; }
    public required int CancelledCount { get; init; }

    /// <summary>Median duration of the day's completed runs; only the per-workflow procedure reads it, and null when none completed.</summary>
    public double? P50DurationMs { get; init; }
}
