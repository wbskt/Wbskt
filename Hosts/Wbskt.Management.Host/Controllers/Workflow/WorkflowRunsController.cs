using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
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
    private readonly IAuthServiceClient _authClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowRunsController> _logger;

    public WorkflowRunsController(
        IWorkflowRunQueryService runQueryService, 
        IWorkflowEngineClient engineClient, 
        IAuthServiceClient authClient,
        [FromKeyedServices(QueuedEventBusExtensions.QueuedKey)] IEventBus eventBus,
        ILogger<WorkflowRunsController> logger)
    {
        _eventBus = eventBus;
        _runQueryService = runQueryService;
        _engineClient = engineClient;
        _authClient = authClient;
        _logger = logger;
    }

    [HttpGet("workflows/{workflowRefId:guid}/runs")]
    public async Task<ActionResult<RunListResponse>> List(Guid workspaceRef, Guid workflowRefId, [FromQuery] string? status, [FromQuery] int top = 50, [FromQuery] long? cursor = null, CancellationToken ct = default)
    {
        _logger.LogDebug("API: List runs requested for WorkspaceRef: '{WorkspaceRef}', WorkflowRefId: '{WorkflowRefId}'", workspaceRef, workflowRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<RunListResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _runQueryService.ListByWorkflowAsync(workspaceIdResult.Value, workflowRefId, status, Paging.Take(top), cursor, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Every run in the workspace, newest first. Without this a dashboard would have to call the
    /// per-workflow list once per workflow to show recent activity.
    /// </summary>
    [HttpGet("runs")]
    public async Task<ActionResult<RunListResponse>> ListForWorkspace(Guid workspaceRef, [FromQuery] string? status, [FromQuery] int top = 50, [FromQuery] long? cursor = null, CancellationToken ct = default)
    {
        _logger.LogDebug("API: List workspace runs requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<RunListResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _runQueryService.ListByWorkspaceAsync(workspaceIdResult.Value, status, Paging.Take(top), cursor, ct);
        return MapResult(result);
    }

    /// <summary>
    /// How a whole workspace is doing. The top-level success rate is run-weighted, so it is dominated
    /// by whichever workflow runs most; the per-workflow breakdown is what makes it readable. Defaults
    /// to the last 30 days.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<WorkspaceStatsResponse>> GetWorkspaceStats(
        Guid workspaceRef,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("API: Workspace stats requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<WorkspaceStatsResponse>.Failure(workspaceIdResult.Error));
        }

        DateTime toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        DateTime fromUtc = (from ?? toUtc.AddDays(-30)).ToUniversalTime();

        var result = await _runQueryService.GetWorkspaceStatsAsync(workspaceIdResult.Value, fromUtc, toUtc, ct);
        return MapResult(result);
    }

    /// <summary>
    /// How a workflow is doing: outcome counts, duration percentiles, success rate, the error codes
    /// that actually occur, and which nodes are slowest. Defaults to the last 30 days.
    /// </summary>
    [HttpGet("workflows/{workflowRefId:guid}/stats")]
    public async Task<ActionResult<WorkflowStatsResponse>> GetStats(
        Guid workspaceRef,
        Guid workflowRefId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("API: Stats requested for WorkspaceRef: '{WorkspaceRef}', WorkflowRefId: '{WorkflowRefId}'", workspaceRef, workflowRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<WorkflowStatsResponse>.Failure(workspaceIdResult.Error));
        }

        DateTime toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        DateTime fromUtc = (from ?? toUtc.AddDays(-30)).ToUniversalTime();

        var result = await _runQueryService.GetStatsAsync(workspaceIdResult.Value, workflowRefId, fromUtc, toUtc, ct);
        return MapResult(result);
    }

    [HttpGet("runs/{runRefId:guid}")]
    public async Task<ActionResult<RunDetailDto>> Get(Guid workspaceRef, Guid runRefId, CancellationToken ct)
    {
        _logger.LogDebug("API: Get run details requested for WorkspaceRef: '{WorkspaceRef}', RunRefId: '{RunRefId}'", workspaceRef, runRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<RunDetailDto>.Failure(workspaceIdResult.Error));
        }

        var result = await _runQueryService.GetDetailAsync(workspaceIdResult.Value, runRefId, ct);
        return MapResult(result);
    }

    [HttpPost("runs/{runRefId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid workspaceRef, Guid runRefId, [FromBody] CancelRunRequest req, CancellationToken ct)
    {
        _logger.LogDebug("API: Cancel run requested for WorkspaceRef: '{WorkspaceRef}', RunRefId: '{RunRefId}'", workspaceRef, runRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsExecute, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var result = await _runQueryService.CancelAsync(workspaceIdResult.Value, runRefId, req.Reason, ct);
        if (result.IsSuccess)
        {
            await _eventBus.PublishAsync(new WorkflowRunCancelRequestedEvent(runRefId, workspaceIdResult.Value, req.Reason), ct);
        }

        return MapResult(result);
    }

    [HttpPost("runs/{runRefId:guid}/signals/{signalName}")]
    public async Task<ActionResult<SignalResponse>> Signal(Guid workspaceRef, Guid runRefId, string signalName, [FromBody] SignalRequest req, CancellationToken ct)
    {
        _logger.LogDebug("API: Send signal '{SignalName}' requested for WorkspaceRef: '{WorkspaceRef}', RunRefId: '{RunRefId}'", signalName, workspaceRef, runRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsExecute, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<SignalResponse>.Failure(workspaceIdResult.Error));
        }

        var ensureRunResult = await _runQueryService.EnsureRunInWorkspaceAsync(workspaceIdResult.Value, runRefId, ct);
        if (ensureRunResult.IsFailure)
        {
            return MapResult(Result<SignalResponse>.Failure(ensureRunResult.Error));
        }

        try
        {
            var response = await _engineClient.SignalAsync(runRefId, signalName, req, ct);
            _logger.LogDebug("Successfully sent signal '{SignalName}' to RunRefId: '{RunRefId}'", signalName, runRefId);
            await _eventBus.PublishAsync(new WorkflowRunSignalSentEvent(runRefId, workspaceIdResult.Value, signalName), ct);
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
