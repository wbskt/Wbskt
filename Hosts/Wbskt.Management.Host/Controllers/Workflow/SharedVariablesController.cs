using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/workspaces/{workspaceRef:guid}/workflows/{workflowRef:guid}/variables")]
[ApiController]
[Authorize]
public sealed class SharedVariablesController : ApiControllerBase
{
    private readonly ISharedVariableService _variableService;

    public SharedVariablesController(ISharedVariableService variableService)
    {
        _variableService = variableService;
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
