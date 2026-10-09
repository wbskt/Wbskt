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

    /// <summary>A page of the run's history, oldest first: <c>cursor</c> and <c>limit</c> (default 200).</summary>
    [HttpGet]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<HistoryListResponse>> List([FromWorkspace] int workspaceId, Guid runRefId, [FromQuery] PageRequest page, CancellationToken ct = default)
    {
        var after = page.AfterKey();
        if (after.IsFailure)
        {
            return MapError(after.Error);
        }

        return MapResult(await _historyService.ListAsync(workspaceId, runRefId, after.Value ?? 0, page.LimitOr(200), ct));
    }
}
