namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// The statuses a run moves through (the <c>dbo.Runs.Status</c> column), and the rules about them.
///
/// <para>The rules live here rather than at each call site so the places that ask "did this run fail?"
/// - the event the run publishes, the severity of its history entry, the run stats - cannot disagree.
/// They did once: an out-of-credits run was recorded as a failure but announced as a success.</para>
/// </summary>
public static class RunStatus
{
    // In flight
    public const string Running = "Running";
    public const string Failing = "Failing";
    public const string Cancelling = "Cancelling";

    // Terminal
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string PartiallyFailed = "PartiallyFailed";
    public const string Cancelled = "Cancelled";
    public const string OutOfCredits = "OutOfCredits";

    /// <summary>Every way a run can end without succeeding. Cancelled is a decision, not a failure.</summary>
    public static bool IsFailure(string status) =>
        status is Failed or PartiallyFailed or OutOfCredits;

    public static bool IsTerminal(string status) =>
        status is Succeeded or Failed or PartiallyFailed or Cancelled or OutOfCredits;
}
