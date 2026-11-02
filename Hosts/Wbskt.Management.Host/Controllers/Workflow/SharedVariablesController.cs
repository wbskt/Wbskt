using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/v1/workspaces/{workspaceRef:guid}/workflows/{workflowRef:guid}/variables")]
[ApiController]
[Authorize]
public sealed class SharedVariablesController : ApiControllerBase
{
    private readonly ISharedVariableService _variableService;

    public SharedVariablesController(ISharedVariableService variableService)
    {
        _variableService = variableService;
    }

    /// <summary>Every shared variable of the workflow, by name, with its type, value and when it last changed.</summary>
    [HttpGet]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<Page<SharedVariableDto>>> List([FromWorkspace] int workspaceId, Guid workflowRef, CancellationToken ct)
    {
        var result = await _variableService.ListAsync(workspaceId, workflowRef, ct);
        if (result.IsFailure)
        {
            return MapResult(Result<Page<SharedVariableDto>>.Failure(result.Error));
        }

        return Ok(new Page<SharedVariableDto> { Items = result.Value, TotalCount = result.Value.Count });
    }

    [HttpGet("{variableName}")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<SharedVariableDto>> Get([FromWorkspace] int workspaceId, Guid workflowRef, string variableName, CancellationToken ct)
    {
        return MapResult(await _variableService.GetAsync(workspaceId, workflowRef, variableName, ct));
    }

    [HttpPut("{variableName}")]
    [RequiresPermission(PermissionNames.WorkflowsExecute)]
    public async Task<ActionResult<SharedVariableDto>> Set([FromWorkspace] int workspaceId, Guid workflowRef, string variableName, [FromBody] SharedVariableSetRequest request, CancellationToken ct)
    {
        return MapResult(await _variableService.SetAsync(workspaceId, workflowRef, variableName, request.ValueJson, ct));
    }
}
