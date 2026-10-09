using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/workspaces/{workspaceRef:guid}")]
[ApiController]
[Authorize]
public sealed class WorkflowRunsController : ApiControllerBase
{
    private readonly IWorkflowRunQueryService _runQueryService;
    private readonly IWorkflowRunService _runService;

    public WorkflowRunsController(IWorkflowRunQueryService runQueryService, IWorkflowRunService runService)
    {
        _runQueryService = runQueryService;
        _runService = runService;
    }

    [HttpGet("workflows/{workflowRef:guid}/runs")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<RunListResponse>> List([FromWorkspace] int workspaceId, Guid workflowRef, [FromQuery] string? status, [FromQuery] PageRequest page, CancellationToken ct = default)
    {
        var cursor = page.AfterKey();
        if (cursor.IsFailure)
        {
            return MapError(cursor.Error);
        }

        var result = await _runQueryService.ListByWorkflowAsync(workspaceId, workflowRef, status, page.LimitOr(50), cursor.Value, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Every run in the workspace, newest first. Without this a dashboard would have to call the
    /// per-workflow list once per workflow to show recent activity.
    /// </summary>
    [HttpGet("runs")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<RunListResponse>> ListForWorkspace([FromWorkspace] int workspaceId, [FromQuery] string? status, [FromQuery] PageRequest page, CancellationToken ct = default)
    {
        var cursor = page.AfterKey();
        if (cursor.IsFailure)
        {
            return MapError(cursor.Error);
        }

        var result = await _runQueryService.ListByWorkspaceAsync(workspaceId, status, page.LimitOr(50), cursor.Value, ct);
        return MapResult(result);
    }

    /// <summary>
    /// How a whole workspace is doing. The top-level success rate is run-weighted, so it is dominated
    /// by whichever workflow runs most; the per-workflow breakdown is what makes it readable. Defaults
    /// to the last 30 days.
    /// </summary>
    [HttpGet("stats")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<WorkspaceStatsResponse>> GetWorkspaceStats(
        [FromWorkspace] int workspaceId,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var result = await _runQueryService.GetWorkspaceStatsAsync(workspaceId, from, to, ct);
        return MapResult(result);
    }

    /// <summary>
    /// How a workflow is doing: outcome counts, duration percentiles, success rate, the error codes
    /// that actually occur, and which nodes are slowest. Defaults to the last 30 days.
    /// </summary>
    [HttpGet("workflows/{workflowRef:guid}/stats")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<WorkflowStatsResponse>> GetStats(
        [FromWorkspace] int workspaceId,
        Guid workflowRef,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var result = await _runQueryService.GetStatsAsync(workspaceId, workflowRef, from, to, ct);
        return MapResult(result);
    }

    [HttpGet("runs/{runRef:guid}")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<RunDetailDto>> Get([FromWorkspace] int workspaceId, Guid runRef, CancellationToken ct)
    {
        var result = await _runQueryService.GetDetailAsync(workspaceId, runRef, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Asks the engine to cancel a run. Cancellation is the engine's to carry out, so this sends it a
    /// command and answers 202 once the broker has it; the run moves to 'Cancelling' and then
    /// 'Cancelled' shortly after. 404 for a run that is unknown or another workspace's, 409 for one
    /// that has already finished, 503 when the broker is unavailable - nothing was sent, so the caller
    /// should retry.
    /// </summary>
    [HttpPost("runs/{runRef:guid}/cancel")]
    [RequiresPermission(PermissionNames.WorkflowsExecute)]
    public async Task<IActionResult> Cancel([FromWorkspace] int workspaceId, Guid runRef, [FromBody] CancelRunRequest req, CancellationToken ct)
    {
        var result = await _runService.CancelAsync(workspaceId, runRef, req.Reason, ct);
        return result.IsSuccess ? Accepted() : MapError(result.Error);
    }

    [HttpPost("runs/{runRef:guid}/signals/{signalName}")]
    [RequiresPermission(PermissionNames.WorkflowsExecute)]
    public async Task<ActionResult<SignalResponse>> Signal([FromWorkspace] int workspaceId, Guid runRef, string signalName, [FromBody] SignalRequest req, CancellationToken ct)
    {
        return MapResult(await _runService.SignalAsync(workspaceId, runRef, signalName, req, ct));
    }
}
