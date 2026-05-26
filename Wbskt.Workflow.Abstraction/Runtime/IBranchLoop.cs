namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IBranchLoop
{
    Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken ct);
}
