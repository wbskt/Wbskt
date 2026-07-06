using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/workspaces/{workspaceRef:guid}")]
[ApiController]
[Authorize]
public sealed class WorkflowRunsController : ControllerBase
{
    private readonly IWorkflowRunQueryService _runQueryService;
    private readonly IWorkflowEngineClient _engineClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<WorkflowRunsController> _logger;

    public WorkflowRunsController(
        IWorkflowRunQueryService runQueryService, 
        IWorkflowEngineClient engineClient, 
        IAuthServiceClient authClient,
        ILogger<WorkflowRunsController> logger)
    {
        _runQueryService = runQueryService;
        _engineClient = engineClient;
        _authClient = authClient;
        _logger = logger;
    }

    [HttpGet("workflows/{workflowRefId:guid}/runs")]
    public async Task<ActionResult<RunListResponse>> List(Guid workspaceRef, Guid workflowRefId, [FromQuery] string? status, [FromQuery] int top = 50, [FromQuery] long? cursor = null, CancellationToken ct = default)
    {
        _logger.LogInformation("API: List runs requested for WorkspaceRef: '{WorkspaceRef}', WorkflowRefId: '{WorkflowRefId}'", workspaceRef, workflowRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<RunListResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _runQueryService.ListByWorkflowAsync(workspaceIdResult.Value, workflowRefId, status, top, cursor, ct);
        return MapResult(result);
    }

    [HttpGet("runs/{runRefId:guid}")]
    public async Task<ActionResult<RunDetailDto>> Get(Guid workspaceRef, Guid runRefId, CancellationToken ct)
    {
        _logger.LogInformation("API: Get run details requested for WorkspaceRef: '{WorkspaceRef}', RunRefId: '{RunRefId}'", workspaceRef, runRefId);

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
        _logger.LogInformation("API: Cancel run requested for WorkspaceRef: '{WorkspaceRef}', RunRefId: '{RunRefId}'", workspaceRef, runRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsUpdate, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var result = await _runQueryService.CancelAsync(workspaceIdResult.Value, runRefId, req.Reason, ct);
        return MapResult(result);
    }

    [HttpPost("runs/{runRefId:guid}/signals/{signalName}")]
    public async Task<ActionResult<SignalResponse>> Signal(Guid workspaceRef, Guid runRefId, string signalName, [FromBody] SignalRequest req, CancellationToken ct)
    {
        _logger.LogInformation("API: Send signal '{SignalName}' requested for WorkspaceRef: '{WorkspaceRef}', RunRefId: '{RunRefId}'", signalName, workspaceRef, runRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsUpdate, ct);
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
            _logger.LogInformation("Successfully sent signal '{SignalName}' to RunRefId: '{RunRefId}'", signalName, runRefId);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error signalling run RunRefId: '{RunRefId}'. Error: {Message}", runRefId, ex.Message);
            _logger.LogTrace(ex, "Signal exception stack trace for RunRefId '{RunRefId}', SignalName '{SignalName}'", runRefId, signalName);
            return MapError(Error.Failure("ENGINE_SIGNAL_ERROR", ex.Message));
        }
    }

    private IActionResult MapResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return MapError(result.Error);
    }

    private ActionResult<T> MapResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapError(result.Error);
    }

    private ActionResult MapError(Error error)
    {
        _logger.LogWarning("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);
        return error.Type switch
        {
            ErrorType.Validation => BadRequest(error),
            ErrorType.NotFound => NotFound(error),
            ErrorType.Conflict => Conflict(error),
            ErrorType.Unauthorized => Unauthorized(error),
            _ => BadRequest(error)
        };
    }
}
