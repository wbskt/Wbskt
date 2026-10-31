using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/v1/workspaces/{workspaceRef:guid}/workflows/{workflowRef:guid}/triggers")]
[ApiController]
[Authorize]
public sealed class WorkflowTriggersController : ApiControllerBase
{
    private readonly ITriggerActivityService _activityService;

    public WorkflowTriggersController(ITriggerActivityService activityService)
    {
        _activityService = activityService;
    }

    /// <summary>
    /// Why a trigger did or did not start a run: what it did with each of the latest messages that
    /// reached it, and, for a client trigger with a hold time, whether it is holding and until when.
    /// </summary>
    [HttpGet("{nodeId:guid}/recent")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<TriggerActivityResponse>> GetRecent([FromWorkspace] int workspaceId, Guid workflowRef, Guid nodeId, CancellationToken ct)
    {
        var result = await _activityService.GetRecentAsync(workspaceId, workflowRef, nodeId, ct);
        return MapResult(result);
    }
}
