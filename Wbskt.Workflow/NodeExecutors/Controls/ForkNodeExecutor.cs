using System.Text.Json;
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

        Guid joinToken = Guid.NewGuid();
        await aggregators.InitializeAsync(joinToken, (int)ctx.Branch.RunId, node.Config.Branches.Count, ct);
        JsonElement joinTokenElement = JsonSerializer.SerializeToElement(joinToken.ToString());

        var children = new List<ForkSpec>();
        foreach (var branch in node.Config.Branches)
        {
            children.Add(new ForkSpec(branch, new Dictionary<string, JsonElement>
            {
                ["__join_token"] = joinTokenElement
            }));
        }

        return new NodeExecutionResult.Fork(children, null, new Dictionary<string, JsonElement>());
    }
}
