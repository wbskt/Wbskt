using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
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
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(
        IWorkflowDefinitionService service, 
        IWorkflowEngineClient engineClient, 
        IAuthServiceClient authClient,
        ILogger<WorkflowsController> logger)
    {
        _service = service;
        _engineClient = engineClient;
        _authClient = authClient;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<WorkflowPublishResponse>> Publish(Guid workspaceRef, [FromBody] WorkflowPublishRequest request, CancellationToken ct)
    {
        _logger.LogInformation("API: Publish requested for WorkspaceRef: '{WorkspaceRef}' (Workflow Name: '{WorkflowName}')", workspaceRef, request.Name);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsCreate, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<WorkflowPublishResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _service.PublishAsync(workspaceIdResult.Value, workspaceRef, request, ct);
        return MapResult(result);
    }

    [HttpGet]
    public async Task<ActionResult<Wbskt.Models.ListResponse<WorkflowSummaryDto>>> GetAll(
        Guid workspaceRef,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        _logger.LogInformation("API: GetAll workflows requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<Wbskt.Models.ListResponse<WorkflowSummaryDto>>.Failure(workspaceIdResult.Error));
        }

        var result = await _service.GetAllSummariesAsync(workspaceIdResult.Value, skip, take, ct);
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
        _logger.LogInformation("API: GetCurrent workflow requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<WorkflowDefinitionDto>.Failure(workspaceIdResult.Error));
        }

        var result = await _service.GetCurrentAsync(workspaceIdResult.Value, refId, ct);
        return MapResult(result);
    }

    [HttpGet("{refId:guid}/versions/{version:int}")]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetVersion(Guid workspaceRef, Guid refId, int version, CancellationToken ct)
    {
        _logger.LogInformation("API: GetVersion workflow requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}' (Version: {Version})", workspaceRef, refId, version);

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
        _logger.LogInformation("API: Deprecate workflow requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsDelete, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var result = await _service.DeprecateAsync(workspaceIdResult.Value, refId, ct);
        return MapResult(result);
    }

    [HttpPost("{refId:guid}/runs")]
    public async Task<ActionResult<StartRunResponse>> StartManualRun(Guid workspaceRef, Guid refId, [FromBody] StartRunRequest request, CancellationToken ct)
    {
        _logger.LogInformation("API: StartManualRun requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

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

        try
        {
            var response = await _engineClient.StartManualRunAsync(refId, request, ct);
            _logger.LogInformation("Successfully started manual run for workflow '{RefId}'", refId);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error starting manual run for workflow '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "StartManualRun exception stack trace for RefId '{RefId}'", refId);
            return MapError(Error.Failure("ENGINE_START_ERROR", ex.Message));
        }
    }



}
