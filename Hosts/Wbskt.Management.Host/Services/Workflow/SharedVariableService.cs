using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class SharedVariableService : ISharedVariableService
{
    private readonly ISharedVariableProvider _variables;
    private readonly IWorkflowQueryService _workflows;
    private readonly IEventBus _eventBus;

    public SharedVariableService(ISharedVariableProvider variables, IWorkflowQueryService workflows, IEventBus eventBus)
    {
        _variables = variables;
        _workflows = workflows;
        _eventBus = eventBus;
    }

    /// <summary>Variable names come from the definition, but every version's names stay, so the list is capped rather than trusted to be short.</summary>
    internal const int MaxListed = 200;

    public async Task<Result<IPagedList<SharedVariableDto>>> ListAsync(int workspaceId, Guid workflowRefId, CancellationToken ct)
    {
        var ensureWorkflowResult = await _workflows.EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);
        if (ensureWorkflowResult.IsFailure)
        {
            return Result<IPagedList<SharedVariableDto>>.Failure(ensureWorkflowResult.Error);
        }

        var rows = await _variables.ListByWorkflowRefIdAsync(workflowRefId, MaxListed, ct);
        return Result<IPagedList<SharedVariableDto>>.Success(new PagedList<SharedVariableDto>(rows.Select(Map), rows.TotalCount));
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

        SharedVariableRow? before = await _variables.FindByWorkflowRefIdNameAsync(workflowRefId, name, ct);
        SharedVariableRow row = await _variables.SetAsync(workflowRefId, name, valueJson, ct);
        await _eventBus.PublishAsync(
            new SharedVariableSetEvent(workflowRefId, workspaceId, row.VarName)
            {
                Changes = [new FieldChange("value", SharedVariableValues.Cut(before?.ValueJson), SharedVariableValues.Cut(row.ValueJson))]
            },
            ct);

        return Result<SharedVariableDto>.Success(Map(row));
    }

    private static SharedVariableDto Map(SharedVariableRow row)
    {
        return new SharedVariableDto(row.WorkflowRefId, row.VarName, row.VarType, row.ValueJson, row.UpdatedAt);
    }
}
