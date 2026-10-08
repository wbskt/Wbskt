using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/workspaces/{workspaceRef:guid}/workflows/{workflowRefId:guid}/variables")]
[ApiController]
[Authorize]
public sealed class SharedVariablesController : ApiControllerBase
{
    private readonly ISharedVariableProvider _variableProvider;
    private readonly IWorkflowDefinitionService _workflowService;
    private readonly ILogger<SharedVariablesController> _logger;

    public SharedVariablesController(
        ISharedVariableProvider variableProvider,
        IWorkflowDefinitionService workflowService,
        ILogger<SharedVariablesController> logger)
    {
        _variableProvider = variableProvider;
        _workflowService = workflowService;
        _logger = logger;
    }

    [HttpGet("{name}")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<SharedVariableDto>> Get([FromWorkspace] int workspaceId, Guid workflowRefId, string name, CancellationToken ct)
    {
        var ensureWorkflowResult = await _workflowService.EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);
        if (ensureWorkflowResult.IsFailure)
        {
            return MapResult(Result<SharedVariableDto>.Failure(ensureWorkflowResult.Error));
        }

        try
        {
            SharedVariableRow row = await _variableProvider.GetByWorkflowRefIdNameAsync(workflowRefId, name, ct);
            return Ok(Map(row));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Shared variable '{Name}' not found for workflow '{WorkflowRefId}'. Error: {Message}", name, workflowRefId, ex.Message);
            _logger.LogTrace(ex, "Get shared variable lookup failure stack trace for '{Name}'", name);
            return NotFound(Error.NotFound("VARIABLE_NOT_FOUND", $"Shared variable '{name}' not found."));
        }
    }

    [HttpPut("{name}")]
    [RequiresPermission(PermissionNames.WorkflowsExecute)]
    public async Task<ActionResult<SharedVariableDto>> Set([FromWorkspace] int workspaceId, Guid workflowRefId, string name, [FromBody] SharedVariableSetRequest request, CancellationToken ct)
    {
        var ensureWorkflowResult = await _workflowService.EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);
        if (ensureWorkflowResult.IsFailure)
        {
            return MapResult(Result<SharedVariableDto>.Failure(ensureWorkflowResult.Error));
        }

        try
        {
            SharedVariableRow row = await _variableProvider.SetAsync(workflowRefId, name, request.ValueJson, ct);
            return Ok(Map(row));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error setting shared variable '{Name}' for workflow '{WorkflowRefId}'. Error: {Message}", name, workflowRefId, ex.Message);
            _logger.LogTrace(ex, "Set exception stack trace for variable '{Name}'", name);
            return MapError(Error.Failure("VARIABLE_SET_ERROR", ex.Message));
        }
    }

    private static SharedVariableDto Map(SharedVariableRow row)
    {
        return new SharedVariableDto(row.WorkflowRefId, row.VarName, row.VarType, row.ValueJson, row.UpdatedAt);
    }



}
