using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/workspaces/{workspaceRef:guid}/runs/{runRefId:guid}/history")]
[ApiController]
[Authorize]
public sealed class WorkflowHistoryController : ApiControllerBase
{
    private readonly IWorkflowHistoryService _historyService;

    public WorkflowHistoryController(IWorkflowHistoryService historyService)
    {
        _historyService = historyService;
    }

    [HttpGet]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<HistoryListResponse>> List([FromWorkspace] int workspaceId, Guid runRefId, [FromQuery] long fromEventId = 0, [FromQuery] int top = 200, CancellationToken ct = default)
    {
        return MapResult(await _historyService.ListAsync(workspaceId, runRefId, fromEventId, Paging.Take(top), ct));
    }
}
