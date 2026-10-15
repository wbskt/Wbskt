namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed record BranchExecutionRequest(long RunId, long BranchId, BranchExecutionReason Reason);

public enum BranchExecutionReason
{
    TriggerStarted,
    ForkChild,
    BookmarkResumed,
    Retry,
    Cancellation,

    // A new leader re-dispatching a branch the previous leader was running when it died. Not a
    // bookmark resume: nothing woke the branch, so its history must not say it resumed.
    Recovered
}
