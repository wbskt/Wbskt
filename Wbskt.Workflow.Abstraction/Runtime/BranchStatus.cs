namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// The statuses a branch moves through (the <c>dbo.Branches.Status</c> column). The stored procedures
/// that select branches by status (Branch_GetRunning, Branch_CancelWaiting, Run_GetStuck) use the same
/// words, so a change here is a change there.
/// </summary>
public static class BranchStatus
{
    // In flight
    public const string Active = "Active";
    public const string Waiting = "Waiting";
    public const string Compensating = "Compensating";

    // Ended
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static bool IsEnded(string status) =>
        status is Completed or Failed or Cancelled;

    /// <summary>
    /// The statuses a branch can be ended from. Branch_EndIfLive spells the same set out in its WHERE
    /// clause, so a change here is a change there.
    /// </summary>
    public static bool IsLive(string status) =>
        status is Active or Waiting or Compensating;
}
