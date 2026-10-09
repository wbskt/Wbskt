using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface ISharedVariableProvider
{
    Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct);

    /// <summary>The variable, or null when the workflow has none by that name. Defaulted so the engine's test doubles need no stub.</summary>
    Task<SharedVariableRow?> FindByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct)
        => throw new NotSupportedException();
    Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct);
    Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct);
    Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct);
    Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct);
    Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct);
}
