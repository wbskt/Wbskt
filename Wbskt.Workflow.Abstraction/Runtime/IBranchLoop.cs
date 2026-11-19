namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IBranchLoop
{
    /// <summary>
    /// Runs the branch until it parks or ends. Once <paramref name="drainToken"/> is cancelled the loop
    /// starts no further node and leaves the branch Active for recovery; the node it is in still
    /// finishes. <paramref name="ct"/> cuts that node short too.
    /// </summary>
    Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken drainToken, CancellationToken ct);

    Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken ct)
    {
        return RunAsync(runId, branchId, reason, CancellationToken.None, ct);
    }
}
