namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed record BranchExecutionRequest(long RunId, long BranchId, BranchExecutionReason Reason);

public enum BranchExecutionReason
{
    TriggerStarted,
    ForkChild,
    BookmarkResumed,
    Retry,
    Cancellation
}
