using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// Everything a result handler knows about the branch whose node just returned: the per-branch half of
/// a handler call, so the handlers themselves hold nothing per branch.
/// </summary>
/// <param name="RunId">The run id the loop was asked to run, as it was asked.</param>
/// <param name="BranchId">The branch id the loop was asked to run, as it was asked.</param>
/// <param name="Run">The run's row, as loaded when the loop started.</param>
/// <param name="Branch">The branch's row as it stood when the node ran.</param>
/// <param name="Definition">The workflow definition the run executes.</param>
/// <param name="Node">The node that produced the result.</param>
/// <param name="NodeContext">The context the node ran with.</param>
internal sealed record BranchResultContext(
    long RunId,
    long BranchId,
    RunRow Run,
    BranchRow Branch,
    WorkflowDefinition Definition,
    BaseNode Node,
    BranchContext NodeContext);

/// <summary>
/// What the loop does after a handler: carry on with the branch at its new position, or stop because
/// the branch ended or parked.
/// </summary>
internal sealed class BranchResultOutcome
{
    private BranchResultOutcome(BranchRow? branch)
    {
        Branch = branch;
    }

    /// <summary>The branch ended or parked; the loop returns.</summary>
    public static BranchResultOutcome Stopped { get; } = new(null);

    /// <summary>The branch carries on at <paramref name="branch"/>'s node.</summary>
    public static BranchResultOutcome Advanced(BranchRow branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return new BranchResultOutcome(branch);
    }

    /// <summary>The branch row the loop carries on with, or null once it has stopped.</summary>
    public BranchRow? Branch { get; }

    public bool IsStopped => Branch is null;
}
