namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunDispatcher
{
    ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct);
}
