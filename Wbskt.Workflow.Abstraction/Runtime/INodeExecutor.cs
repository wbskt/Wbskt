namespace Wbskt.Workflow.Abstraction.Runtime;

public interface INodeExecutor
{
    string Kind { get; }

    Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct);

    bool IsSideEffectFree => false;
}
