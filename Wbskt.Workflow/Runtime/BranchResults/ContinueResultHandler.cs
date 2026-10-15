using Microsoft.Extensions.Logging;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// <see cref="NodeExecutionResult.Continue"/>: moves the branch to the node its port leads to, with the
/// node's patch merged into its local state, or completes the branch when the port leads nowhere - a
/// cohort branch declining its Join first.
/// </summary>
internal sealed class ContinueResultHandler(
    IBranchProvider branchProvider,
    BranchEndings endings,
    JoinDeclines declines) : BranchResultHandler<NodeExecutionResult.Continue>
{
    public override async Task<BranchResultOutcome> HandleAsync(NodeExecutionResult.Continue result, BranchResultContext context, CancellationToken ct)
    {
        Guid? nextNodeId = BranchRouting.ResolveNext(context.Definition, context.Node.NodeId, result.OutboundPort);
        IReadOnlyDictionary<string, JsonElement> localState = BranchLocalState.Merge(context.Branch.LocalJson, result.LocalStatePatch, result.RemoveKeys);
        if (nextNodeId is null)
        {
            // The state the node leaves behind decides the cohort: a Join's winner whose port leads
            // nowhere has already handed its token back to the enclosing cohort, and declines that one.
            await declines.DeclineAsync(context.Run.Id, context.Branch, context.Node.NodeId, localState, ct);
            await endings.CompleteAsync(context.Run.Id, context.BranchId, context.Branch.RefId, ct);
            return BranchResultOutcome.Stopped;
        }

        string localJson = BranchLocalState.Serialize(localState);
        BranchRow moved = await branchProvider.UpdatePointerAsync(context.BranchId, nextNodeId.Value, BranchStatus.Active, localJson, context.Branch.LastOutputJson, ct);
        return BranchResultOutcome.Advanced(moved);
    }
}
