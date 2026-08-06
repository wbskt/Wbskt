using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

internal sealed class ParallelForEachNodeExecutor : INodeExecutor
{
    private readonly IExpressionEvaluator _expressionEvaluator;
    private readonly IJoinAggregatorProvider _aggregators;

    public ParallelForEachNodeExecutor(IExpressionEvaluator expressionEvaluator, IJoinAggregatorProvider aggregators)
    {
        _expressionEvaluator = expressionEvaluator;
        _aggregators = aggregators;
    }

    public string Kind => NodeKind.ControlParallelForEach;

    public bool IsSideEffectFree => true;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not ParallelForEachNode node || node.Config is null || string.IsNullOrWhiteSpace(node.Config.Collection))
        {
            return new NodeExecutionResult.Fail("PFE_CONFIG_INVALID", "ParallelForEach node config is required.", false, null);
        }

        JsonElement collection = await _expressionEvaluator.EvaluateAsync(new BranchStateRefExpression(node.Config.Collection), ctx.Branch, ct);
        if (collection.ValueKind != JsonValueKind.Array)
        {
            return new NodeExecutionResult.Fail("PFE_COLLECTION_NOT_ARRAY", $"ParallelForEach collection evaluated to {collection.ValueKind} instead of array.", false, null);
        }

        List<JsonElement> items = collection.EnumerateArray().ToList();

        if (items.Count == 0)
        {
            return new NodeExecutionResult.Continue("empty", new Dictionary<string, JsonElement>());
        }

        // Resolve the Join this cohort converges on and stamp its config onto the aggregator. Doing
        // it here (rather than passing config on each contribution) is what lets a FAILED branch
        // contribute later from BranchLoop, which never sees the Join node.
        JoinNode? joinNode = ctx.Definition is null
            ? null
            : WorkflowGraph.FindDownstream<JoinNode>(ctx.Definition, node.NodeId, "body");
        if (joinNode is null)
        {
            return new NodeExecutionResult.Fail(
                "PFE_NO_JOIN",
                $"ParallelForEach node '{node.NodeId}' has no Join node downstream of its 'body' port; the cohort could never converge.",
                false,
                null);
        }

        Guid joinToken = Guid.NewGuid();
        await _aggregators.InitializeAsync(
            joinToken,
            (int)ctx.Branch.RunId,
            items.Count,
            (joinNode.Config?.Mode ?? JoinMode.All).ToString(),
            joinNode.Config?.QuorumCount ?? 0,
            joinNode.NodeId,
            ct);

        JsonElement joinTokenElement = JsonSerializer.SerializeToElement(joinToken.ToString());

        List<ForkSpec> children = [];
        for (int i = 0; i < items.Count; i++)
        {
            children.Add(new ForkSpec("body", new Dictionary<string, JsonElement>
            {
                ["item"] = items[i].Clone(),
                ["__join_token"] = joinTokenElement,
                ["__join_index"] = JsonSerializer.SerializeToElement(i)
            }));
        }

        return new NodeExecutionResult.Fork(children, null, new Dictionary<string, JsonElement>());
    }
}
