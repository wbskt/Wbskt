using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/workspaces/{workspaceRef:guid}/workflows/{workflowRefId:guid}/variables")]
[ApiController]
[Authorize]
public sealed class SharedVariablesController : ApiControllerBase
{
    private readonly ISharedVariableService _variableService;

    public SharedVariablesController(ISharedVariableService variableService)
    {
        _variableService = variableService;
    }

    [HttpGet("{name}")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<SharedVariableDto>> Get([FromWorkspace] int workspaceId, Guid workflowRefId, string name, CancellationToken ct)
    {
        return MapResult(await _variableService.GetAsync(workspaceId, workflowRefId, name, ct));
    }

    [HttpPut("{name}")]
    [RequiresPermission(PermissionNames.WorkflowsExecute)]
    public async Task<ActionResult<SharedVariableDto>> Set([FromWorkspace] int workspaceId, Guid workflowRefId, string name, [FromBody] SharedVariableSetRequest request, CancellationToken ct)
    {
        return MapResult(await _variableService.SetAsync(workspaceId, workflowRefId, name, request.ValueJson, ct));
    }
}
