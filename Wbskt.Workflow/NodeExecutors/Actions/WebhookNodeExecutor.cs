using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

public sealed class WebhookNodeExecutor : INodeExecutor
{
    public string Kind => NodeKind.ActionWebhook;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ctx;
        _ = ct;
        throw new NotImplementedException("WebhookNodeExecutor not yet implemented - Phase 9 TODO");
    }
}
