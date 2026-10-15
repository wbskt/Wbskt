using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// <see cref="NodeExecutionResult.Terminal"/>: ends the branch, cancelled when the reason is
/// <see cref="BranchTerminalReason.Cancelled"/> (a run cancelled under the node) and completed otherwise.
/// A cohort branch completed anywhere but at its Join declines the Join first.
/// </summary>
internal sealed class TerminalResultHandler(BranchEndings endings, JoinDeclines declines) : BranchResultHandler<NodeExecutionResult.Terminal>
{
    public override async Task<BranchResultOutcome> HandleAsync(NodeExecutionResult.Terminal result, BranchResultContext context, CancellationToken ct)
    {
        if (result.Reason == BranchTerminalReason.Cancelled)
        {
            await endings.CancelAsync(context.Run.Id, context.BranchId, context.Branch, ct);
        }
        else
        {
            // A Join that ends a branch has already counted it (it was not the last to arrive).
            if (context.Node is not JoinNode)
            {
                await declines.DeclineAsync(context.Run.Id, context.Branch, context.Node.NodeId, BranchLocalState.Deserialize(context.Branch.LocalJson), ct);
            }

            await endings.CompleteAsync(context.Run.Id, context.BranchId, context.Branch.RefId, ct);
        }

        return BranchResultOutcome.Stopped;
    }
}
