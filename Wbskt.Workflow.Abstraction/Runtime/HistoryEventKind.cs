namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// The kinds of entry that make up a run's history, and how severe each one is.
///
/// <para>Severity is not cosmetic: <c>HistoryEvent_DeleteForRetiredRuns</c> keeps Warn/Error entries
/// far longer than Info ones, so getting it wrong either discards the record of a failure or retains
/// routine chatter forever. It lives here rather than at each call site so the two cannot disagree.</para>
/// </summary>
public static class HistoryEventKind
{
    // Run lifecycle
    public const string RunStarted = "RunStarted";
    public const string RunFinalized = "RunFinalized";
    public const string RunFaulted = "RunFaulted";
    public const string RunCancellationRequested = "RunCancellationRequested";

    // Branch lifecycle
    public const string BranchStarted = "BranchStarted";
    public const string BranchResumed = "BranchResumed";
    public const string BranchParked = "BranchParked";
    public const string BranchCompleted = "BranchCompleted";
    public const string BranchFailed = "BranchFailed";
    public const string BranchCancelled = "BranchCancelled";

    // Node execution
    public const string NodeStarted = "NodeStarted";
    public const string NodeCompleted = "NodeCompleted";
    public const string NodeFailed = "NodeFailed";
    public const string NodeRetrying = "NodeRetrying";

    // Fan-out / fan-in
    public const string JoinContinuationSpawned = "JoinContinuationSpawned";

    // Compensation
    public const string CompensationExecuted = "CompensationExecuted";
    public const string CompensationFailed = "CompensationFailed";

    public const string SeverityInfo = "Info";
    public const string SeverityWarn = "Warn";
    public const string SeverityError = "Error";

    /// <summary>Severity for a kind; unknown kinds are Info.</summary>
    public static string SeverityFor(string eventKind)
    {
        return eventKind switch
        {
            // A run that faulted is the engine failing, not the workflow.
            RunFaulted => SeverityError,

            NodeFailed
                or BranchFailed
                or NodeRetrying
                or CompensationFailed
                or BranchCancelled
                or RunCancellationRequested => SeverityWarn,

            _ => SeverityInfo
        };
    }
}
