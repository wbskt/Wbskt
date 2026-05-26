namespace Wbskt.Workflow.Abstraction.Runtime;

public interface INodeExecutor
{
    Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct);
}
