using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers.Workflow;

/// <summary>
/// A workflow's unpublished changes, kept on the server so they follow the author to another device and
/// are visible to the workspace. One draft per workflow; a workflow never published exists only as one.
/// </summary>
[Route("api/v1/workspaces/{workspaceRef:guid}/workflows")]
[ApiController]
[Authorize]
public sealed class WorkflowDraftsController : ApiControllerBase
{
    private readonly IWorkflowDraftService _draftService;
    private readonly IWorkflowTestRunService _testRunService;

    public WorkflowDraftsController(IWorkflowDraftService draftService, IWorkflowTestRunService testRunService)
    {
        _draftService = draftService;
        _testRunService = testRunService;
    }

    [HttpGet("drafts")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<Page<WorkflowDraftSummaryDto>>> GetAll([FromWorkspace] int workspaceId, CancellationToken ct)
    {
        return Ok(new Page<WorkflowDraftSummaryDto> { Items = await _draftService.ListAsync(workspaceId, ct) });
    }

    [HttpGet("{workflowRef:guid}/draft")]
    [RequiresPermission(PermissionNames.WorkflowsRead)]
    public async Task<ActionResult<WorkflowDraftDto>> Get([FromWorkspace] int workspaceId, Guid workflowRef, CancellationToken ct)
    {
        return MapResult(await _draftService.GetAsync(workspaceId, workflowRef, ct));
    }

    // Drafting is authoring, so it takes the permission publishing does.
    [HttpPut("{workflowRef:guid}/draft")]
    [RequiresPermission(PermissionNames.WorkflowsCreate)]
    public async Task<ActionResult<WorkflowDraftDto>> Save([FromWorkspace] int workspaceId, Guid workflowRef, [FromBody] WorkflowDraftSaveRequest request, CancellationToken ct)
    {
        return MapResult(await _draftService.SaveAsync(workspaceId, workflowRef, request, ct));
    }

    [HttpDelete("{workflowRef:guid}/draft")]
    [RequiresPermission(PermissionNames.WorkflowsCreate)]
    public async Task<IActionResult> Discard([FromWorkspace] int workspaceId, Guid workflowRef, [FromQuery] int? revision, CancellationToken ct)
    {
        return MapResult(await _draftService.DiscardAsync(workspaceId, workflowRef, revision, ct));
    }

    [HttpPost("{workflowRef:guid}/draft/publish")]
    [RequiresPermission(PermissionNames.WorkflowsCreate)]
    public async Task<ActionResult<WorkflowPublishResponse>> Publish(Guid workspaceRef, [FromWorkspace] int workspaceId, Guid workflowRef, [FromBody] WorkflowDraftPublishRequest request, CancellationToken ct)
    {
        return MapResult(await _draftService.PublishAsync(workspaceId, workspaceRef, workflowRef, request, ct));
    }

    /// <summary>
    /// Runs the draft or the live version by hand, whatever its trigger, with the trigger data given.
    /// Its steps' side effects are real. One test run at a time per workflow.
    /// </summary>
    // A test run is authoring as well as running: it can run a workflow nobody has published.
    [HttpPost("{workflowRef:guid}/test-runs")]
    [RequiresPermission(PermissionNames.WorkflowsExecute, PermissionNames.WorkflowsCreate)]
    public async Task<ActionResult<StartTestRunResponse>> StartTestRun([FromWorkspace] int workspaceId, Guid workflowRef, [FromBody] StartTestRunRequest request, CancellationToken ct)
    {
        return MapResult(await _testRunService.StartAsync(workspaceId, workflowRef, request, ct));
    }
}
