namespace Wbskt.Workflow.Abstraction.Runtime;

// The ids are long here and in the runtime interfaces but int in the entities and the database, so
// callers narrow them with (int) casts. Which width wins is settled with the move of the id columns
// to BIGINT, not piecemeal before it.
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
