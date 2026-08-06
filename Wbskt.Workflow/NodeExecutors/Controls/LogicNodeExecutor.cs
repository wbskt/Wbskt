using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

internal sealed class LogicNodeExecutor : INodeExecutor
{
    private readonly IExpressionEvaluator _expressionEvaluator;

    public LogicNodeExecutor(IExpressionEvaluator expressionEvaluator)
    {
        _expressionEvaluator = expressionEvaluator;
    }

    public string Kind => NodeKind.ControlLogic;

    public bool IsSideEffectFree => true;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not LogicGateNode node || node.Config?.Condition is null)
        {
            return new NodeExecutionResult.Fail("LOGIC_CONFIG_INVALID", "Logic node config is required.", false, null);
        }

        // The condition is a full expression tree, so comparisons and functions work here. A
        // malformed one raises ExpressionEvaluationException, which the retry executor turns into a
        // non-retryable EXPRESSION_EVALUATION_ERROR naming the offending types.
        JsonElement evaluation = await _expressionEvaluator.EvaluateAsync(node.Config.Condition, ctx.Branch, ct);
        if (evaluation.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return new NodeExecutionResult.Fail("LOGIC_CONDITION_NOT_BOOL", $"Logic condition evaluated to {evaluation.ValueKind} instead of bool.", false, null);
        }

        return new NodeExecutionResult.Continue(evaluation.GetBoolean() ? "true" : "false", new Dictionary<string, JsonElement>());
    }
}
