using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Explicit terminal node. Completes the branch; when the run's last active branch completes,
/// the run finalizes (Succeeded unless other branches failed). A configured output is evaluated
/// against the branch here, where its local state is at hand, and carried on the terminal result;
/// the terminal handler records it on the run for a waiting parent to read.
/// </summary>
/// <param name="evaluator">
/// Optional so the executor's unit tests need no stub. Only required when the node declares an output;
/// without one, such a node fails with a clear message rather than handing the parent nothing.
/// </param>
internal sealed class EndNodeExecutor(IExpressionEvaluator? evaluator = null) : INodeExecutor
{
    public string Kind => NodeKind.ControlEnd;

    public bool IsReplaySafe(BaseNode node) => true;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not EndNode { Config.Output: { } outputExpression })
        {
            return new NodeExecutionResult.Terminal(BranchTerminalReason.Completed);
        }

        JsonElement output;
        try
        {
            if (evaluator is null)
            {
                throw new InvalidOperationException("No expression evaluator is available to evaluate the End node's output.");
            }

            output = (await evaluator.EvaluateAsync(outputExpression, ctx.Branch, ct)).Clone();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An output that cannot be evaluated is an authoring error that a retry would repeat.
            // Completing without it would hand the parent a child that "succeeded" with nothing to read,
            // which is harder to trace than the branch failing here, on the node that is wrong.
            return new NodeExecutionResult.Fail(
                "END_OUTPUT_EVALUATION_ERROR",
                $"The output of End node '{ctx.Node.NodeId}' could not be evaluated: {ex.Message}",
                false,
                null);
        }

        return new NodeExecutionResult.Terminal(BranchTerminalReason.Completed) { Output = output };
    }
}
