using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

public sealed class WaitForHttpNodeExecutor : INodeExecutor
{
    public string Kind => NodeKind.ControlWaitForHttp;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ctx;
        _ = ct;
        throw new NotImplementedException("WaitForHttpNodeExecutor not yet implemented - Phase 9 TODO");
    }
}
