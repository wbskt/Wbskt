using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Iterates a collection <b>one item at a time</b> on a single branch.
///
/// <para>The node is revisited once per lap: the body's tail wires back to this node's input, and an
/// iterator index in local state tracks progress. Each visit either hands the next item out through
/// "body", or - once the collection is exhausted - leaves through "done" and clears the iterator so a
/// nested/outer loop can run the whole thing again.</para>
///
/// <para>This is the sequential counterpart to <see cref="ParallelForEachNodeExecutor"/>, which fans
/// every item out at once. It previously behaved identically to the parallel node, and additionally
/// took "done" immediately at fan-out - so anything wired after the loop ran before a single item had
/// been processed.</para>
/// </summary>
internal sealed class ForEachNodeExecutor : INodeExecutor
{
    private readonly IExpressionEvaluator _expressionEvaluator;

    public ForEachNodeExecutor(IExpressionEvaluator expressionEvaluator)
    {
        _expressionEvaluator = expressionEvaluator;
    }

    public string Kind => NodeKind.ControlForEach;

    public bool IsSideEffectFree => true;

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

        // Keyed by node id so nested loops - and a loop revisited by an outer loop - keep separate
        // counters rather than trampling a single shared one.
        string iteratorKey = IteratorKey(node.NodeId);
        int index = ReadIndex(ctx, iteratorKey);

        List<JsonElement> items = collection.EnumerateArray().ToList();

        if (index >= items.Count)
        {
            // Exhausted. Drop the iterator so a later re-entry starts from zero again.
            return new NodeExecutionResult.Continue(
                "done",
                new Dictionary<string, JsonElement>(),
                RemoveKeys: [iteratorKey]);
        }

        return new NodeExecutionResult.Continue("body", new Dictionary<string, JsonElement>
        {
            ["item"] = items[index].Clone(),
            ["index"] = JsonSerializer.SerializeToElement(index),
            [iteratorKey] = JsonSerializer.SerializeToElement(index + 1)
        });
    }

    internal static string IteratorKey(Guid nodeId)
    {
        return $"__foreach:{nodeId:N}:index";
    }

    private static int ReadIndex(NodeContext ctx, string iteratorKey)
    {
        if (ctx.Branch.LocalState.TryGetValue(iteratorKey, out JsonElement stored)
            && stored.ValueKind == JsonValueKind.Number
            && stored.TryGetInt32(out int index)
            && index >= 0)
        {
            return index;
        }

        return 0;
    }
}
