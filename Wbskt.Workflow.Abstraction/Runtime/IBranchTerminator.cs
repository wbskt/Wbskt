namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// What happens after branches end, whichever path ended them: their slots in the run's active-branch
/// count are released, and the run is finalized when that leaves nothing running. The caller has
/// already written the branch's own status and history.
/// </summary>
public interface IBranchTerminator
{
    /// <summary>
    /// Releases <paramref name="count"/> ended branches and returns how many are still active. A count of
    /// zero releases nothing and still finalizes a run with nothing running, for a caller that ended no
    /// branch itself but may have left the run idle.
    /// </summary>
    Task<int> ReleaseAsync(int runId, int count, CancellationToken ct);
}
