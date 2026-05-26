using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

public sealed class CommandNodeExecutor : INodeExecutor
{
    public string Kind => NodeKind.ActionCommand;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ctx;
        _ = ct;
        throw new NotImplementedException("CommandNodeExecutor not yet implemented - Phase 9 TODO");
    }
}
