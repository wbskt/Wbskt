using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Ends a branch left Waiting with no bookmark to wake it, so its run can converge instead of holding
/// its slot and credits forever.
/// </summary>
public interface IStrandedBranchFailer
{
    /// <summary>
    /// Re-checks <paramref name="branch"/> and, if it is still Waiting, unchanged and without a bookmark,
    /// fails it with <c>BOOKMARK_LOST</c> the way a failing node would. In a run that is already
    /// failing or cancelling it re-applies that stop instead, which cancels the branch.
    /// </summary>
    /// <returns><c>false</c> when the branch had moved on and nothing was done.</returns>
    Task<bool> FailAsync(BranchRow branch, CancellationToken ct);
}
