using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

public sealed class ForEachNodeExecutor : INodeExecutor
{
    private readonly IExpressionEvaluator _expressionEvaluator;

    public ForEachNodeExecutor(IExpressionEvaluator expressionEvaluator)
    {
        _expressionEvaluator = expressionEvaluator;
    }

    public string Kind => NodeKind.ControlForEach;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not ForEachNode node || node.Config is null || string.IsNullOrWhiteSpace(node.Config.Collection))
        {
            return new NodeExecutionResult.Fail("FOREACH_CONFIG_INVALID", "ForEach node config is required.", false, null);
        }

        JsonElement collection = await _expressionEvaluator.EvaluateAsync(new BranchStateRefExpression(node.Config.Collection), ctx.Branch, ct);
        if (collection.ValueKind != JsonValueKind.Array)
        {
            return new NodeExecutionResult.Fail("FOREACH_COLLECTION_NOT_ARRAY", $"ForEach collection evaluated to {collection.ValueKind} instead of array.", false, null);
        }

        List<ForkSpec> children = [];
        foreach (JsonElement item in collection.EnumerateArray())
        {
            children.Add(new ForkSpec("body", new Dictionary<string, JsonElement>
            {
                ["item"] = item.Clone()
            }));
        }

        return new NodeExecutionResult.Fork(children, "done", new Dictionary<string, JsonElement>());
    }
}
