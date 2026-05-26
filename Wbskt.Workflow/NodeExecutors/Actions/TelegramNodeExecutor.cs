using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

public sealed class TelegramNodeExecutor : INodeExecutor
{
    public string Kind => NodeKind.ActionTelegram;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ctx;
        _ = ct;
        throw new NotImplementedException("TelegramNodeExecutor not yet implemented - Phase 9 TODO");
    }
}
