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

    public bool IsSideEffectFree => true;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not ForkNode node || node.Config is null)
        {
            return new NodeExecutionResult.Fail("FORK_CONFIG_INVALID", "Fork node config is required.", false, null);
        }

        // A Fork's branches converge on the same Join, so the first one reachable from any branch
        // port identifies the cohort's Join. Stamping its config onto the aggregator is what lets a
        // branch that FAILS contribute later from BranchLoop, which never sees the Join node.
        // A Fork with no Join downstream is legal - the branches simply run to their own ends - so
        // this stays null rather than failing the node.
        JoinNode? joinNode = null;
        if (ctx.Definition is not null)
        {
            foreach (string branchPort in node.Config.Branches)
            {
                joinNode = WorkflowGraph.FindDownstream<JoinNode>(ctx.Definition, node.NodeId, branchPort);
                if (joinNode is not null)
                {
                    break;
                }
            }
        }

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
