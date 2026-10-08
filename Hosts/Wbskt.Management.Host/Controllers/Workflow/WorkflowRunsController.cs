using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Infrastructure.Events;
using Wbskt.Management.Host.Services.Clients;
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
    private readonly IWorkflowEngineClient _engineClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowRunsController> _logger;

    public WorkflowRunsController(
        IWorkflowRunQueryService runQueryService, 
        IWorkflowEngineClient engineClient, 
        [FromKeyedServices(QueuedEventBusExtensions.QueuedKey)] IEventBus eventBus,
        ILogger<WorkflowRunsController> logger)
    {
        _eventBus = eventBus;
        _runQueryService = runQueryService;
        _engineClient = engineClient;
        _logger = logger;
    }

    [HttpGet("workflows/{workflowRefId:guid}/runs")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<RunListResponse>> List([FromWorkspace] int workspaceId, Guid workflowRefId, [FromQuery] string? status, [FromQuery] int top = 50, [FromQuery] long? cursor = null, CancellationToken ct = default)
    {
        var result = await _runQueryService.ListByWorkflowAsync(workspaceId, workflowRefId, status, Paging.Take(top), cursor, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Every run in the workspace, newest first. Without this a dashboard would have to call the
    /// per-workflow list once per workflow to show recent activity.
    /// </summary>
    [HttpGet("runs")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<RunListResponse>> ListForWorkspace([FromWorkspace] int workspaceId, [FromQuery] string? status, [FromQuery] int top = 50, [FromQuery] long? cursor = null, CancellationToken ct = default)
    {
        var result = await _runQueryService.ListByWorkspaceAsync(workspaceId, status, Paging.Take(top), cursor, ct);
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
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        DateTime toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        DateTime fromUtc = (from ?? toUtc.AddDays(-30)).ToUniversalTime();

        var result = await _runQueryService.GetWorkspaceStatsAsync(workspaceId, fromUtc, toUtc, ct);
        return MapResult(result);
    }

    /// <summary>
    /// How a workflow is doing: outcome counts, duration percentiles, success rate, the error codes
    /// that actually occur, and which nodes are slowest. Defaults to the last 30 days.
    /// </summary>
    [HttpGet("workflows/{workflowRefId:guid}/stats")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<WorkflowStatsResponse>> GetStats(
        [FromWorkspace] int workspaceId,
        Guid workflowRefId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        DateTime toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        DateTime fromUtc = (from ?? toUtc.AddDays(-30)).ToUniversalTime();

        var result = await _runQueryService.GetStatsAsync(workspaceId, workflowRefId, fromUtc, toUtc, ct);
        return MapResult(result);
    }

    [HttpGet("runs/{runRefId:guid}")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<RunDetailDto>> Get([FromWorkspace] int workspaceId, Guid runRefId, CancellationToken ct)
    {
        var result = await _runQueryService.GetDetailAsync(workspaceId, runRefId, ct);
        return MapResult(result);
    }

    [HttpPost("runs/{runRefId:guid}/cancel")]
    [RequiresPermission(PermissionNames.WorkflowsExecute)]
    public async Task<IActionResult> Cancel([FromWorkspace] int workspaceId, Guid runRefId, [FromBody] CancelRunRequest req, CancellationToken ct)
    {
        var result = await _runQueryService.CancelAsync(workspaceId, runRefId, req.Reason, ct);
        if (result.IsSuccess)
        {
            await _eventBus.PublishAsync(new WorkflowRunCancelRequestedEvent(runRefId, workspaceId, req.Reason), ct);
        }

        return MapResult(result);
    }

    [HttpPost("runs/{runRefId:guid}/signals/{signalName}")]
    [RequiresPermission(PermissionNames.WorkflowsExecute)]
    public async Task<ActionResult<SignalResponse>> Signal([FromWorkspace] int workspaceId, Guid runRefId, string signalName, [FromBody] SignalRequest req, CancellationToken ct)
    {
        var ensureRunResult = await _runQueryService.EnsureRunInWorkspaceAsync(workspaceId, runRefId, ct);
        if (ensureRunResult.IsFailure)
        {
            return MapResult(Result<SignalResponse>.Failure(ensureRunResult.Error));
        }

        try
        {
            var response = await _engineClient.SignalAsync(runRefId, signalName, req, ct);
            _logger.LogDebug("Successfully sent signal '{SignalName}' to RunRefId: '{RunRefId}'", signalName, runRefId);
            await _eventBus.PublishAsync(new WorkflowRunSignalSentEvent(runRefId, workspaceId, signalName), ct);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error signalling run RunRefId: '{RunRefId}'. Error: {Message}", runRefId, ex.Message);
            _logger.LogTrace(ex, "Signal exception stack trace for RunRefId '{RunRefId}', SignalName '{SignalName}'", runRefId, signalName);
            return MapError(Error.Failure("ENGINE_SIGNAL_ERROR", ex.Message));
        }
    }



}
