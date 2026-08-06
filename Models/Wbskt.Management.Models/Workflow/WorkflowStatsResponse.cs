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
