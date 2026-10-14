using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// <see cref="NodeExecutionResult.Terminal"/>: ends the branch, cancelled when the reason is
/// <see cref="BranchTerminalReason.Cancelled"/> (a run cancelled under the node) and completed otherwise.
/// </summary>
internal sealed class TerminalResultHandler(
    IBranchProvider branchProvider,
    IRunJournal journal,
    IBranchTerminator branchTerminator) : BranchResultHandler<NodeExecutionResult.Terminal>
{
    private readonly BranchEndings _endings = new(branchProvider, journal, branchTerminator);

    public override async Task<BranchResultOutcome> HandleAsync(NodeExecutionResult.Terminal result, BranchResultContext context, CancellationToken ct)
    {
        if (result.Reason == BranchTerminalReason.Cancelled)
        {
            await _endings.CancelAsync(context.Run.Id, context.BranchId, context.Branch, ct);
        }
        else
        {
            await _endings.CompleteAsync(context.Run.Id, context.BranchId, context.Branch.RefId, ct);
        }

        return BranchResultOutcome.Stopped;
    }
}
