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

[Route("api/workspaces/{workspaceRef:guid}/workflows")]
[ApiController]
[Authorize]
public sealed class WorkflowsController : ApiControllerBase
{
    private readonly IWorkflowDefinitionService _service;
    private readonly IWorkflowEngineClient _engineClient;
    private readonly IAuthServiceClient _authClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(
        IWorkflowDefinitionService service, 
        IWorkflowEngineClient engineClient, 
        IAuthServiceClient authClient,
        [FromKeyedServices(QueuedEventBusExtensions.QueuedKey)] IEventBus eventBus,
        ILogger<WorkflowsController> logger)
    {
        _eventBus = eventBus;
        _service = service;
        _engineClient = engineClient;
        _authClient = authClient;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<WorkflowPublishResponse>> Publish(Guid workspaceRef, [FromBody] WorkflowPublishRequest request, CancellationToken ct)
    {
        _logger.LogDebug("API: Publish requested for WorkspaceRef: '{WorkspaceRef}' (Workflow Name: '{WorkflowName}')", workspaceRef, request.Name);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsCreate, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<WorkflowPublishResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _service.PublishAsync(workspaceIdResult.Value, workspaceRef, request, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Checks a definition without publishing it. Publishing is the only other way to run the
    /// validator, and it creates an immutable version and re-registers triggers - far too heavy for
    /// "is this draft OK?".
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<WorkflowValidationResponse>> ValidateDefinition(Guid workspaceRef, [FromBody] WorkflowPublishRequest request, CancellationToken ct)
    {
        _logger.LogDebug("API: Validate requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        // Same permission as publishing: this is an authoring operation, and it reveals which rules
        // a definition breaks.
        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsCreate, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<WorkflowValidationResponse>.Failure(workspaceIdResult.Error));
        }

        // An invalid definition is a successful answer to "is this valid?" - 200 with IsValid false,
        // not an HTTP error.
        return Ok(_service.Validate(request.Definition));
    }

    [HttpGet]
    public async Task<ActionResult<Wbskt.Models.ListResponse<WorkflowSummaryDto>>> GetAll(
        Guid workspaceRef,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        _logger.LogDebug("API: GetAll workflows requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<Wbskt.Models.ListResponse<WorkflowSummaryDto>>.Failure(workspaceIdResult.Error));
        }

        var result = await _service.GetAllSummariesAsync(workspaceIdResult.Value, Paging.Skip(skip), Paging.Take(take), ct);
        if (result.IsFailure)
        {
            return MapResult(Result<Wbskt.Models.ListResponse<WorkflowSummaryDto>>.Failure(result.Error));
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());

        return Ok(new Wbskt.Models.ListResponse<WorkflowSummaryDto>
        {
            Items = result.Value
        });
    }

    [HttpGet("{refId:guid}")]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetCurrent(Guid workspaceRef, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("API: GetCurrent workflow requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<WorkflowDefinitionDto>.Failure(workspaceIdResult.Error));
        }

        var result = await _service.GetCurrentAsync(workspaceIdResult.Value, refId, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Every published version of a workflow, newest first, with how many runs each has had. Fetch a
    /// version's definition with <c>GET versions/{version}</c>.
    /// </summary>
    [HttpGet("{refId:guid}/versions")]
    public async Task<ActionResult<Wbskt.Models.ListResponse<WorkflowVersionDto>>> GetVersions(Guid workspaceRef, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("API: GetVersions workflow requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<Wbskt.Models.ListResponse<WorkflowVersionDto>>.Failure(workspaceIdResult.Error));
        }

        var result = await _service.GetVersionsAsync(workspaceIdResult.Value, refId, ct);
        if (result.IsFailure)
        {
            return MapResult(Result<Wbskt.Models.ListResponse<WorkflowVersionDto>>.Failure(result.Error));
        }

        return Ok(new Wbskt.Models.ListResponse<WorkflowVersionDto> { Items = result.Value });
    }

    [HttpGet("{refId:guid}/versions/{version:int}")]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetVersion(Guid workspaceRef, Guid refId, int version, CancellationToken ct)
    {
        _logger.LogDebug("API: GetVersion workflow requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}' (Version: {Version})", workspaceRef, refId, version);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<WorkflowDefinitionDto>.Failure(workspaceIdResult.Error));
        }

        var result = await _service.GetVersionAsync(workspaceIdResult.Value, refId, version, ct);
        return MapResult(result);
    }

    [HttpPost("{refId:guid}/deprecate")]
    public async Task<IActionResult> Deprecate(Guid workspaceRef, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("API: Deprecate workflow requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsDelete, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var result = await _service.DeprecateAsync(workspaceIdResult.Value, refId, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Deletes a workflow. Unlike deprecating, it cannot be undone: the workflow leaves the list, its
    /// triggers stop, runs still going are cancelled, and its RefId cannot be published again. Past
    /// runs and the versions they ran stay readable by run.
    /// </summary>
    [HttpDelete("{refId:guid}")]
    public async Task<IActionResult> Delete(Guid workspaceRef, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("API: Delete workflow requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsDelete, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var result = await _service.DeleteAsync(workspaceIdResult.Value, refId, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Undoes a deprecate. Deprecating deregisters the workflow's triggers, so this re-registers them
    /// (schedules are re-seeded from their cron) as well as flipping the flag back.
    /// </summary>
    [HttpPost("{refId:guid}/reinstate")]
    public async Task<IActionResult> Reinstate(Guid workspaceRef, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("API: Reinstate workflow requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        // Bringing a workflow back into service is an authoring change, so it takes the same
        // permission as deprecating it.
        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsDelete, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var result = await _service.ReinstateAsync(workspaceIdResult.Value, workspaceRef, refId, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Republishes an earlier version as a new version. The history stays append-only, so the runs of
    /// every version keep pointing at the definition they actually ran.
    /// </summary>
    [HttpPost("{refId:guid}/rollback/{version:int}")]
    public async Task<ActionResult<WorkflowPublishResponse>> Rollback(Guid workspaceRef, Guid refId, int version, CancellationToken ct)
    {
        _logger.LogDebug("API: Rollback requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}' to version {Version}", workspaceRef, refId, version);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsCreate, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<WorkflowPublishResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _service.RollbackAsync(workspaceIdResult.Value, workspaceRef, refId, version, ct);
        return MapResult(result);
    }

    [HttpPost("{refId:guid}/runs")]
    public async Task<ActionResult<StartRunResponse>> StartManualRun(Guid workspaceRef, Guid refId, [FromBody] StartRunRequest request, CancellationToken ct)
    {
        _logger.LogDebug("API: StartManualRun requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsExecute, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<StartRunResponse>.Failure(workspaceIdResult.Error));
        }

        // Validates the workflow belongs to the workspace before delegating to the engine.
        var getWorkflowResult = await _service.GetCurrentAsync(workspaceIdResult.Value, refId, ct);
        if (getWorkflowResult.IsFailure)
        {
            return MapResult(Result<StartRunResponse>.Failure(getWorkflowResult.Error));
        }

        // A deprecated workflow still resolves as "current" (the lookup returns the latest version
        // regardless of IsEnabled), but its triggers were deregistered - so the engine would find no
        // registration and the caller would get an opaque failure. Say so plainly instead.
        if (getWorkflowResult.Value.Status != "Published")
        {
            _logger.LogInformation("Rejected manual run for deprecated workflow '{RefId}'", refId);
            return MapError(Error.Conflict("WORKFLOW_DEPRECATED", $"Workflow '{refId}' is deprecated and cannot be started."));
        }

        StartRunResponse response;
        try
        {
            response = await _engineClient.StartManualRunAsync(refId, request, ct);
        }
        catch (Exception ex)
        {
            // Only a genuine transport/engine fault reaches here now.
            _logger.LogError("Unexpected error starting manual run for workflow '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "StartManualRun exception stack trace for RefId '{RefId}'", refId);
            return MapError(Error.Failure("ENGINE_START_ERROR", ex.Message));
        }

        _logger.LogInformation("Manual run request for workflow '{RefId}' resulted in {Outcome}", refId, response.Outcome);
        if (response.Outcome is StartRunOutcome.Started or StartRunOutcome.Queued or StartRunOutcome.Duplicate)
        {
            await _eventBus.PublishAsync(new WorkflowRunRequestedEvent(refId, workspaceIdResult.Value, response.RunRefId, response.Outcome.ToString()), ct);
        }

        return response.Outcome switch
        {
            // A deduplicated retry is a success from the caller's point of view - it returns the run
            // the original call started rather than a second one.
            StartRunOutcome.Started or StartRunOutcome.Duplicate => Ok(response),

            // Accepted but not yet running: the concurrency policy is holding it behind an active run.
            StartRunOutcome.Queued => Accepted(response),

            StartRunOutcome.Dropped => MapError(Error.Conflict(
                "RUN_DROPPED_BY_CONCURRENCY_POLICY",
                $"Workflow '{refId}' already has an active run and its concurrency policy discarded this request.")),

            StartRunOutcome.NoManualTrigger => MapError(Error.Conflict(
                "WORKFLOW_HAS_NO_MANUAL_TRIGGER",
                $"Workflow '{refId}' has no manual trigger, so it cannot be started this way.")),

            _ => MapError(Error.Failure("ENGINE_START_ERROR", $"Unrecognised engine outcome '{response.Outcome}'."))
        };
    }



}
