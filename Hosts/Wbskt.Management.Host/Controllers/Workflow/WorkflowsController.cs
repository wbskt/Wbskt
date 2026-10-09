using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/workspaces/{workspaceRef:guid}/workflows")]
[ApiController]
[Authorize]
public sealed class WorkflowsController : ApiControllerBase
{
    private readonly IWorkflowDefinitionService _service;
    private readonly IWorkflowRunService _runService;

    public WorkflowsController(IWorkflowDefinitionService service, IWorkflowRunService runService)
    {
        _service = service;
        _runService = runService;
    }

    [HttpPost]
    [RequiresPermission(PermissionNames.WorkflowsCreate)]
    public async Task<ActionResult<WorkflowPublishResponse>> Publish(Guid workspaceRef, [FromWorkspace] int workspaceId, [FromBody] WorkflowPublishRequest request, CancellationToken ct)
    {
        var result = await _service.PublishAsync(workspaceId, workspaceRef, request, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Checks a definition without publishing it. Publishing is the only other way to run the
    /// validator, and it creates an immutable version and re-registers triggers - far too heavy for
    /// "is this draft OK?".
    /// </summary>
    [HttpPost("validate")]
    // Same permission as publishing: this is an authoring operation, and it reveals which rules a
    // definition breaks.
    [RequiresPermission(PermissionNames.WorkflowsCreate)]
    public ActionResult<WorkflowValidationResponse> ValidateDefinition([FromBody] WorkflowPublishRequest request)
    {
        // An invalid definition is a successful answer to "is this valid?" - 200 with IsValid false,
        // not an HTTP error.
        return Ok(_service.Validate(request.Definition));
    }

    [HttpGet]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<Page<WorkflowSummaryDto>>> GetAll(
        [FromWorkspace] int workspaceId,
        [FromQuery] PageRequest page,
        CancellationToken ct = default)
    {
        var offset = page.Offset();
        if (offset.IsFailure)
        {
            return MapError(offset.Error);
        }

        var result = await _service.GetAllSummariesAsync(workspaceId, offset.Value, page.LimitOr(100), ct);
        return MapPage(result, offset.Value);
    }

    [HttpGet("{refId:guid}")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetCurrent([FromWorkspace] int workspaceId, Guid refId, CancellationToken ct)
    {
        var result = await _service.GetCurrentAsync(workspaceId, refId, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Every published version of a workflow, newest first, with how many runs each has had. Fetch a
    /// version's definition with <c>GET versions/{version}</c>.
    /// </summary>
    [HttpGet("{refId:guid}/versions")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<Page<WorkflowVersionDto>>> GetVersions([FromWorkspace] int workspaceId, Guid refId, CancellationToken ct)
    {
        var result = await _service.GetVersionsAsync(workspaceId, refId, ct);
        if (result.IsFailure)
        {
            return MapResult(Result<Page<WorkflowVersionDto>>.Failure(result.Error));
        }

        return Ok(new Page<WorkflowVersionDto> { Items = result.Value });
    }

    [HttpGet("{refId:guid}/versions/{version:int}")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetVersion([FromWorkspace] int workspaceId, Guid refId, int version, CancellationToken ct)
    {
        var result = await _service.GetVersionAsync(workspaceId, refId, version, ct);
        return MapResult(result);
    }

    [HttpPost("{refId:guid}/deprecate")]
    [RequiresPermission(PermissionNames.WorkflowsDelete)]
    public async Task<IActionResult> Deprecate([FromWorkspace] int workspaceId, Guid refId, CancellationToken ct)
    {
        var result = await _service.DeprecateAsync(workspaceId, refId, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Deletes a workflow. Unlike deprecating, it cannot be undone: the workflow leaves the list, its
    /// triggers stop, runs still going are cancelled, and its RefId cannot be published again. Past
    /// runs and the versions they ran stay readable by run.
    /// </summary>
    [HttpDelete("{refId:guid}")]
    [RequiresPermission(PermissionNames.WorkflowsDelete)]
    public async Task<IActionResult> Delete([FromWorkspace] int workspaceId, Guid refId, CancellationToken ct)
    {
        var result = await _service.DeleteAsync(workspaceId, refId, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Undoes a deprecate. Deprecating deregisters the workflow's triggers, so this re-registers them
    /// (schedules are re-seeded from their cron) as well as flipping the flag back.
    /// </summary>
    [HttpPost("{refId:guid}/reinstate")]
    [RequiresPermission(PermissionNames.WorkflowsDelete)]
    public async Task<IActionResult> Reinstate(Guid workspaceRef, [FromWorkspace] int workspaceId, Guid refId, CancellationToken ct)
    {
        // Bringing a workflow back into service is an authoring change, so it takes the same
        // permission as deprecating it.
        var result = await _service.ReinstateAsync(workspaceId, workspaceRef, refId, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Republishes an earlier version as a new version. The history stays append-only, so the runs of
    /// every version keep pointing at the definition they actually ran.
    /// </summary>
    [HttpPost("{refId:guid}/rollback/{version:int}")]
    [RequiresPermission(PermissionNames.WorkflowsCreate)]
    public async Task<ActionResult<WorkflowPublishResponse>> Rollback(Guid workspaceRef, [FromWorkspace] int workspaceId, Guid refId, int version, CancellationToken ct)
    {
        var result = await _service.RollbackAsync(workspaceId, workspaceRef, refId, version, ct);
        return MapResult(result);
    }

    [HttpPost("{refId:guid}/runs")]
    [RequiresPermission(PermissionNames.WorkflowsExecute)]
    public async Task<ActionResult<StartRunResponse>> StartManualRun([FromWorkspace] int workspaceId, Guid refId, [FromBody] StartRunRequest request, CancellationToken ct)
    {
        var result = await _runService.StartManualAsync(workspaceId, refId, request, ct);
        if (result.IsFailure)
        {
            return MapError(result.Error);
        }

        // Accepted but not yet running: the concurrency policy is holding it behind an active run.
        return result.Value.Outcome == StartRunOutcome.Queued ? Accepted(result.Value) : Ok(result.Value);
    }
}
