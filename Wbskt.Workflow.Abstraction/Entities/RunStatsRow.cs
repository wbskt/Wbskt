namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>Aggregate run outcomes for a workflow over a window.</summary>
public sealed record RunStatsRow
{
    public required int TotalRuns { get; init; }
    public required int SucceededCount { get; init; }
    public required int FailedCount { get; init; }
    public required int PartiallyFailedCount { get; init; }
    public required int CancelledCount { get; init; }
    public required int FaultedCount { get; init; }
    public required int OutOfCreditsCount { get; init; }

    /// <summary>Runs still in flight — counted, but excluded from the duration figures.</summary>
    public required int ActiveCount { get; init; }

    public required double P50DurationMs { get; init; }
    public required double P95DurationMs { get; init; }
    public required double MaxDurationMs { get; init; }
    public required double AvgDurationMs { get; init; }
}

/// <summary>
/// One workflow's contribution to a workspace rollup. Failure counts are collapsed into a single
/// <see cref="FailedCount"/> - the breakdown belongs on the per-workflow endpoint, and a dashboard row
/// only needs to know whether this workflow is the one dragging the workspace down.
/// </summary>
public sealed record WorkflowRunSummaryRow
{
    public required Guid WorkflowRefId { get; init; }
    public required int TotalRuns { get; init; }
    public required int SucceededCount { get; init; }

    /// <summary>Failed, PartiallyFailed, Faulted and OutOfCredits together - every way of not succeeding.</summary>
    public required int FailedCount { get; init; }

    public required int ActiveCount { get; init; }
    public required double AvgDurationMs { get; init; }
}

/// <summary>One error code seen at one node, with how often and how recently.</summary>
public sealed record RunFailureBucketRow
{
    public required string ErrorCode { get; init; }
    public required Guid? NodeId { get; init; }
    public required int Occurrences { get; init; }
    public required DateTime LastSeenAt { get; init; }
}

/// <summary>Per-node timing, from the durationMs recorded on each node outcome.</summary>
public sealed record NodeTimingRow
{
    public required Guid NodeId { get; init; }
    public required int Executions { get; init; }
    public required int FailureCount { get; init; }
    public required double AvgDurationMs { get; init; }
    public required double MaxDurationMs { get; init; }
}
