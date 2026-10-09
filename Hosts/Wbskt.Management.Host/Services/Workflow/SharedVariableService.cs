using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class SharedVariableService : ISharedVariableService
{
    private readonly ISharedVariableProvider _variables;
    private readonly IWorkflowQueryService _workflows;

    public SharedVariableService(ISharedVariableProvider variables, IWorkflowQueryService workflows)
    {
        _variables = variables;
        _workflows = workflows;
    }

    public async Task<Result<SharedVariableDto>> GetAsync(int workspaceId, Guid workflowRefId, string name, CancellationToken ct)
    {
        var ensureWorkflowResult = await _workflows.EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);
        if (ensureWorkflowResult.IsFailure)
        {
            return Result<SharedVariableDto>.Failure(ensureWorkflowResult.Error);
        }

        SharedVariableRow? row = await _variables.FindByWorkflowRefIdNameAsync(workflowRefId, name, ct);
        return row is null
            ? Result<SharedVariableDto>.Failure(Error.NotFound("VARIABLE_NOT_FOUND", $"Shared variable '{name}' not found."))
            : Result<SharedVariableDto>.Success(Map(row));
    }

    public async Task<Result<SharedVariableDto>> SetAsync(int workspaceId, Guid workflowRefId, string name, string valueJson, CancellationToken ct)
    {
        var ensureWorkflowResult = await _workflows.EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);
        if (ensureWorkflowResult.IsFailure)
        {
            return Result<SharedVariableDto>.Failure(ensureWorkflowResult.Error);
        }

        SharedVariableRow row = await _variables.SetAsync(workflowRefId, name, valueJson, ct);
        return Result<SharedVariableDto>.Success(Map(row));
    }

    private static SharedVariableDto Map(SharedVariableRow row)
    {
        return new SharedVariableDto(row.WorkflowRefId, row.VarName, row.VarType, row.ValueJson, row.UpdatedAt);
    }
}
