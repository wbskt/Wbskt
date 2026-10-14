using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// <see cref="NodeExecutionResult.Continue"/>: moves the branch to the node its port leads to, with the
/// node's patch merged into its local state, or completes the branch when the port leads nowhere.
/// </summary>
internal sealed class ContinueResultHandler(
    IBranchProvider branchProvider,
    IRunJournal journal,
    IBranchTerminator branchTerminator) : BranchResultHandler<NodeExecutionResult.Continue>
{
    private readonly BranchEndings _endings = new(branchProvider, journal, branchTerminator);

    public override async Task<BranchResultOutcome> HandleAsync(NodeExecutionResult.Continue result, BranchResultContext context, CancellationToken ct)
    {
        Guid? nextNodeId = BranchRouting.ResolveNext(context.Definition, context.Node.NodeId, result.OutboundPort);
        if (nextNodeId is null)
        {
            await _endings.CompleteAsync(context.Run.Id, context.BranchId, context.Branch.RefId, ct);
            return BranchResultOutcome.Stopped;
        }

        string localJson = BranchLocalState.Serialize(BranchLocalState.Merge(context.Branch.LocalJson, result.LocalStatePatch, result.RemoveKeys));
        BranchRow moved = await branchProvider.UpdatePointerAsync(context.BranchId, nextNodeId.Value, BranchStatus.Active, localJson, context.Branch.LastOutputJson, ct);
        return BranchResultOutcome.Advanced(moved);
    }
}
