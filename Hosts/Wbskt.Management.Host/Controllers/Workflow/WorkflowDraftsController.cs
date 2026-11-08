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

    public WorkflowDraftsController(IWorkflowDraftService draftService)
    {
        _draftService = draftService;
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
}
