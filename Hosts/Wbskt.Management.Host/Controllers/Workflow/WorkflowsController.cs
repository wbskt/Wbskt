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
    private readonly IWorkflowQueryService _queryService;
    private readonly IWorkflowLifecycleService _lifecycleService;
    private readonly IWorkflowRunService _runService;

    public WorkflowsController(IWorkflowQueryService queryService, IWorkflowLifecycleService lifecycleService, IWorkflowRunService runService)
    {
        _queryService = queryService;
        _lifecycleService = lifecycleService;
        _runService = runService;
    }

    [HttpPost]
    [RequiresPermission(PermissionNames.WorkflowsCreate)]
    public async Task<ActionResult<WorkflowPublishResponse>> Publish(Guid workspaceRef, [FromWorkspace] int workspaceId, [FromBody] WorkflowPublishRequest request, CancellationToken ct)
    {
        var result = await _lifecycleService.PublishAsync(workspaceId, workspaceRef, request, ct);
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
        return Ok(_queryService.Validate(request.Definition));
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

        var result = await _queryService.GetAllSummariesAsync(workspaceId, offset.Value, page.LimitOr(100), ct);
        return MapPage(result, offset.Value);
    }

    [HttpGet("{workflowRef:guid}")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetCurrent([FromWorkspace] int workspaceId, Guid workflowRef, CancellationToken ct)
    {
        var result = await _queryService.GetCurrentAsync(workspaceId, workflowRef, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Every published version of a workflow, newest first, with how many runs each has had. Fetch a
    /// version's definition with <c>GET versions/{version}</c>.
    /// </summary>
    [HttpGet("{workflowRef:guid}/versions")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<Page<WorkflowVersionDto>>> GetVersions([FromWorkspace] int workspaceId, Guid workflowRef, CancellationToken ct)
    {
        var result = await _queryService.GetVersionsAsync(workspaceId, workflowRef, ct);
        if (result.IsFailure)
        {
            return MapResult(Result<Page<WorkflowVersionDto>>.Failure(result.Error));
        }

        return Ok(new Page<WorkflowVersionDto> { Items = result.Value });
    }

    [HttpGet("{workflowRef:guid}/versions/{version:int}")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetVersion([FromWorkspace] int workspaceId, Guid workflowRef, int version, CancellationToken ct)
    {
        var result = await _queryService.GetVersionAsync(workspaceId, workflowRef, version, ct);
        return MapResult(result);
    }

    [HttpPost("{workflowRef:guid}/deprecate")]
    [RequiresPermission(PermissionNames.WorkflowsDelete)]
    public async Task<IActionResult> Deprecate([FromWorkspace] int workspaceId, Guid workflowRef, CancellationToken ct)
    {
        var result = await _lifecycleService.DeprecateAsync(workspaceId, workflowRef, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Deletes a workflow. Unlike deprecating, it cannot be undone: the workflow leaves the list, its
    /// triggers stop, runs still going are cancelled, and its RefId cannot be published again. Past
    /// runs and the versions they ran stay readable by run.
    /// </summary>
    [HttpDelete("{workflowRef:guid}")]
    [RequiresPermission(PermissionNames.WorkflowsDelete)]
    public async Task<IActionResult> Delete([FromWorkspace] int workspaceId, Guid workflowRef, CancellationToken ct)
    {
        var result = await _lifecycleService.DeleteAsync(workspaceId, workflowRef, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Undoes a deprecate. Deprecating deregisters the workflow's triggers, so this re-registers them
    /// (schedules are re-seeded from their cron) as well as flipping the flag back.
    /// </summary>
    [HttpPost("{workflowRef:guid}/reinstate")]
    [RequiresPermission(PermissionNames.WorkflowsDelete)]
    public async Task<IActionResult> Reinstate(Guid workspaceRef, [FromWorkspace] int workspaceId, Guid workflowRef, CancellationToken ct)
    {
        // Bringing a workflow back into service is an authoring change, so it takes the same
        // permission as deprecating it.
        var result = await _lifecycleService.ReinstateAsync(workspaceId, workspaceRef, workflowRef, ct);
        return MapResult(result);
    }

    /// <summary>
    /// Republishes an earlier version as a new version. The history stays append-only, so the runs of
    /// every version keep pointing at the definition they actually ran.
    /// </summary>
    [HttpPost("{workflowRef:guid}/rollback/{version:int}")]
    [RequiresPermission(PermissionNames.WorkflowsCreate)]
    public async Task<ActionResult<WorkflowPublishResponse>> Rollback(Guid workspaceRef, [FromWorkspace] int workspaceId, Guid workflowRef, int version, CancellationToken ct)
    {
        var result = await _lifecycleService.RollbackAsync(workspaceId, workspaceRef, workflowRef, version, ct);
        return MapResult(result);
    }

    [HttpPost("{workflowRef:guid}/runs")]
    [RequiresPermission(PermissionNames.WorkflowsExecute)]
    public async Task<ActionResult<StartRunResponse>> StartManualRun([FromWorkspace] int workspaceId, Guid workflowRef, [FromBody] StartRunRequest request, CancellationToken ct)
    {
        var result = await _runService.StartManualAsync(workspaceId, workflowRef, request, ct);
        if (result.IsFailure)
        {
            return MapError(result.Error);
        }

        // Accepted but not yet running: the concurrency policy is holding it behind an active run.
        return result.Value.Outcome == StartRunOutcome.Queued ? Accepted(result.Value) : Ok(result.Value);
    }
}
