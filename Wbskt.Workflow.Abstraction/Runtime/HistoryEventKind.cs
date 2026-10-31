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
    // Not written any more; kept so a history row of this kind still reads with its severity.
    public const string RunFaulted = "RunFaulted";
    public const string RunCancellationRequested = "RunCancellationRequested";
    public const string RunFailureRequested = "RunFailureRequested";

    // Branch lifecycle
    public const string BranchStarted = "BranchStarted";
    public const string BranchResumed = "BranchResumed";
    // A new leader picking up a branch that was running when the previous leader went away.
    public const string BranchRecovered = "BranchRecovered";
    public const string BranchParked = "BranchParked";
    public const string BranchCompleted = "BranchCompleted";
    public const string BranchFailed = "BranchFailed";
    public const string BranchCancelled = "BranchCancelled";

    // Node execution
    public const string NodeStarted = "NodeStarted";
    public const string NodeCompleted = "NodeCompleted";
    public const string NodeFailed = "NodeFailed";
    public const string NodeRetrying = "NodeRetrying";
    // An action step's {{ }} placeholder had no value and was sent as empty text.
    public const string TemplateValueMissing = "TemplateValueMissing";
    // A counter step with a fraction, applied as a whole number.
    public const string CounterStepTruncated = "CounterStepTruncated";
    // A local counter whose value was not a number, counted from 0.
    public const string CounterReset = "CounterReset";

    // Fan-out / fan-in
    public const string JoinContinuationSpawned = "JoinContinuationSpawned";
    // A cohort branch ended (an End node, a port with no edge) without reaching its Join.
    public const string JoinAbandoned = "JoinAbandoned";

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
                or TemplateValueMissing
                or CounterStepTruncated
                or CounterReset
                or BranchFailed
                or NodeRetrying
                or BranchCancelled
                or JoinAbandoned
                or RunCancellationRequested
                or RunFailureRequested => SeverityWarn,

            _ => SeverityInfo
        };
    }
}
