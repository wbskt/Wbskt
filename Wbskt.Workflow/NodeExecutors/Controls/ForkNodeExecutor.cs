using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

internal sealed class ForkNodeExecutor : INodeExecutor
{
    public string Kind => NodeKind.ControlFork;

    public bool IsSideEffectFree => true;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ct;

        if (ctx.Node is not ForkNode node || node.Config is null)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Fail("FORK_CONFIG_INVALID", "Fork node config is required.", false, null));
        }

        var children = new List<ForkSpec>();
        foreach (var branch in node.Config.Branches)
        {
            children.Add(new ForkSpec(branch, new Dictionary<string, JsonElement>()));
        }

        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Fork(children, null, new Dictionary<string, JsonElement>()));
    }
}
