namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>A workspace's runs started in one UTC hour (<c>Run_GetHourlyStatsBy_WorkspaceId</c>).</summary>
public sealed record RunHourlyStatsRow
{
    public required DateTime Hour { get; init; }
    public required int TotalRuns { get; init; }
    public required int SucceededCount { get; init; }

    /// <summary>Every way of not succeeding: failed, partially failed, out of credits.</summary>
    public required int FailedCount { get; init; }
    public required int CancelledCount { get; init; }
}
