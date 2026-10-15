using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// <see cref="NodeExecutionResult.Terminal"/>: ends the branch, cancelled when the reason is
/// <see cref="BranchTerminalReason.Cancelled"/> (a run cancelled under the node) and completed otherwise.
/// A cohort branch completed anywhere but at its Join declines the Join first. An output carried by
/// the result (a <c>control:end</c> node that declares one) is recorded on the run before the branch
/// completes, so the finalizer that the last branch's completion runs reads it back with the run.
/// </summary>
/// <param name="runProvider">Optional so the handler's unit tests need no stub; required to record an output.</param>
internal sealed class TerminalResultHandler(BranchEndings endings, JoinDeclines declines, IRunProvider? runProvider = null) : BranchResultHandler<NodeExecutionResult.Terminal>
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

            if (result.Output is { } output && runProvider is not null)
            {
                await runProvider.SetOutputAsync(context.Run.Id, JsonSerializer.Serialize(output, EngineJson.Options), ct);
            }

            await endings.CompleteAsync(context.Run.Id, context.BranchId, context.Branch.RefId, ct);
        }

        return BranchResultOutcome.Stopped;
    }
}
