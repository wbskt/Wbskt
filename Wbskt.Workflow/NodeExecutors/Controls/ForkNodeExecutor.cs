using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

internal sealed class ForkNodeExecutor(IJoinAggregatorProvider aggregators) : INodeExecutor
{
    public string Kind => NodeKind.ControlFork;

    // Not replay-safe, so it keeps the default. Every execution initialises a join aggregator under a
    // fresh token (JoinAggregator_Initialize is a plain INSERT), so a replay would open a second
    // cohort and hand its children a token the first one never sees. The recorded outcome carries the
    // first token, which is the one the aggregator row was written for.

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not ForkNode node || node.Config is null)
        {
            return new NodeExecutionResult.Fail("FORK_CONFIG_INVALID", "Fork node config is required.", false, null);
        }

        // The cohort's Join is the one its branches reach at the Fork's own nesting level - not the
        // nearest one, which for a Fork nested in a branch is the inner Join. Stamping its config onto
        // the aggregator is what lets a branch that FAILS contribute later from BranchLoop, which never
        // sees the Join node. A Fork with no Join at its level is a fire-and-forget fan-out: its
        // branches simply run to their own ends, so this stays null rather than failing the node, and
        // a branch that ends there has no join to abandon.
        JoinNode? joinNode = ctx.Definition is null
            ? null
            : WorkflowGraph.FindJoinForCohort(ctx.Definition, node.NodeId, node.Config.Branches);

        Guid joinToken = Guid.NewGuid();
        await aggregators.InitializeAsync(
            joinToken,
            (int)ctx.Branch.RunId,
            node.Config.Branches.Count,
            (joinNode?.Config?.Mode ?? JoinMode.All).ToString(),
            joinNode?.Config?.QuorumCount ?? 0,
            joinNode?.NodeId,
            ct);
        JsonElement joinTokenElement = JsonSerializer.SerializeToElement(joinToken.ToString());

        var children = new List<ForkSpec>();
        foreach (var branch in node.Config.Branches)
        {
            children.Add(new ForkSpec(branch, JoinTokens.ForChild(ctx.Branch.LocalState, joinTokenElement)));
        }

        return new NodeExecutionResult.Fork(children, null, new Dictionary<string, JsonElement>());
    }
}
