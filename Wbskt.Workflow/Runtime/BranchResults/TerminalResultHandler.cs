using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// <see cref="NodeExecutionResult.Terminal"/>: ends the branch, cancelled when the reason is
/// <see cref="BranchTerminalReason.Cancelled"/> (a run cancelled under the node) and completed otherwise.
/// </summary>
internal sealed class TerminalResultHandler(BranchEndings endings) : BranchResultHandler<NodeExecutionResult.Terminal>
{
    public override async Task<BranchResultOutcome> HandleAsync(NodeExecutionResult.Terminal result, BranchResultContext context, CancellationToken ct)
    {
        if (result.Reason == BranchTerminalReason.Cancelled)
        {
            await endings.CancelAsync(context.Run.Id, context.BranchId, context.Branch, ct);
        }
        else
        {
            await endings.CompleteAsync(context.Run.Id, context.BranchId, context.Branch.RefId, ct);
        }

        return BranchResultOutcome.Stopped;
    }
}
