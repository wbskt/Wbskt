using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

public sealed class EndNodeExecutor : INodeExecutor
{
    public string Kind => NodeKind.ControlEnd;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ctx;
        _ = ct;
        throw new NotImplementedException("EndNodeExecutor not yet implemented - Phase 9 TODO");
    }
}
