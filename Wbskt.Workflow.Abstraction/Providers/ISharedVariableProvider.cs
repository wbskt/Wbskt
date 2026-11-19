using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface ISharedVariableProvider
{
    Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct);

    /// <summary>The variable, or null when the workflow has none by that name. Defaulted so the engine's test doubles need no stub.</summary>
    Task<SharedVariableRow?> FindByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct)
        => throw new NotSupportedException();

    /// <summary>The workflow's first <paramref name="take"/> variables by name, and how many it has. Defaulted so the engine's test doubles need no stub.</summary>
    Task<IPagedList<SharedVariableRow>> ListByWorkflowRefIdAsync(Guid workflowRefId, int take, CancellationToken ct)
        => throw new NotSupportedException();

    Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct);
    Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct);

    /// <summary>
    /// Sets the variable, creating it with <paramref name="varType"/> when it does not exist: last writer
    /// wins, even when two writers create it at once. The default sets and creates on a miss, which is
    /// enough for a single-threaded fake; the SQL provider does it in one procedure.
    /// </summary>
    async Task<SharedVariableRow> UpsertAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct)
    {
        try
        {
            return await SetAsync(workflowRefId, varName, valueJson, ct);
        }
        catch (KeyNotFoundException)
        {
            return await InitializeAsync(workflowRefId, varName, varType, valueJson, ct);
        }
    }

    Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct);
    Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct);
    Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct);
}
