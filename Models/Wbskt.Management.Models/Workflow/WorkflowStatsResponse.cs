namespace Wbskt.Management.Models.Workflow;

/// <summary>How runs of a workflow turned out over a window.</summary>
public record RunOutcomeCountsDto(
    int Total,
    int Succeeded,
    int Failed,
    int PartiallyFailed,
    int Cancelled,
    int Faulted,
    int OutOfCredits,
    int Active);

/// <summary>
/// Run durations in milliseconds, over <b>completed</b> runs only — including in-flight ones as zero
/// would make a busy workflow look fast.
/// </summary>
public record RunDurationsDto(double P50Ms, double P95Ms, double MaxMs, double AvgMs);

public record FailureBucketDto(string ErrorCode, Guid? NodeId, int Occurrences, DateTime LastSeenAt);

public record NodeTimingDto(Guid NodeId, int Executions, int FailureCount, double AvgDurationMs, double MaxDurationMs);

/// <summary>
/// The answer to "how is this workflow doing?".
/// </summary>
/// <param name="SuccessRate">
/// Succeeded ÷ finished, where finished excludes still-running runs. Null when nothing has finished
/// in the window — a rate of 0 would read as "everything failed" rather than "nothing to report".
/// </param>
public record WorkflowStatsResponse(
    Guid WorkflowRefId,
    DateTime FromUtc,
    DateTime ToUtc,
    RunOutcomeCountsDto Counts,
    RunDurationsDto Durations,
    double? SuccessRate,
    IReadOnlyCollection<FailureBucketDto> TopFailures,
    IReadOnlyCollection<NodeTimingDto> SlowestNodes);

/// <summary>One workflow's line in a workspace rollup.</summary>
/// <param name="Failed">Every way of not succeeding — failed, partially failed, faulted, out of credits.</param>
/// <param name="SuccessRate">Succeeded ÷ finished for this workflow alone; null when nothing finished.</param>
public record WorkflowRunSummaryDto(
    Guid WorkflowRefId,
    int Total,
    int Succeeded,
    int Failed,
    int Active,
    double AvgDurationMs,
    double? SuccessRate);

/// <summary>
/// The answer to "how is this workspace doing?".
/// </summary>
/// <param name="SuccessRate">
/// <b>Run-weighted</b>: succeeded ÷ finished across every workflow, which answers "what fraction of the
/// work in this workspace succeeded". It is therefore dominated by whichever workflow runs most — one
/// busy workflow at 99% will hide a quiet one at 0%. That is not a defect to be averaged away: the
/// alternative, a mean of per-workflow rates, lets a workflow with two runs count as much as one with
/// two hundred thousand. Both mislead alone, so <paramref name="Workflows"/> ships alongside and is where
/// the hidden failure is visible. Null when nothing has finished, for the same reason as the
/// per-workflow figure.
/// </param>
/// <param name="Workflows">Per-workflow breakdown, highest volume first.</param>
public record WorkspaceStatsResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    RunOutcomeCountsDto Counts,
    RunDurationsDto Durations,
    double? SuccessRate,
    IReadOnlyCollection<WorkflowRunSummaryDto> Workflows);
