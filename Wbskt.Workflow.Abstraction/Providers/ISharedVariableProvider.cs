using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface ISharedVariableProvider
{
    Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct);
    Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct);
    Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct);
    Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct);
    Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct);
    Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct);
}
