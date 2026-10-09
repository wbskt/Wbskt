using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/message-templates")]
[ApiController]
[Authorize]
public sealed class MessageTemplatesController : ApiControllerBase
{
    private readonly IMessageTemplateService _templateService;

    public MessageTemplatesController(
        IMessageTemplateService templateService)
    {
        _templateService = templateService;
    }

    /// <summary>
    /// Retrieves the saved send-panel message templates for a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="policyRefId">Optional filter: only templates pinned to this policy.</param>
    /// <param name="page">The page to read: <c>cursor</c> and <c>limit</c> (default 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of message templates.</returns>
    [HttpGet]
    [RequiresPermission(PermissionNames.TemplatesRead)]
    public async Task<ActionResult<Page<MessageTemplateResponse>>> GetAll(
        [FromWorkspace] int workspaceId,
        [FromQuery] Guid? policyRefId,
        [FromQuery] PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var offset = page.Offset();
        if (offset.IsFailure)
        {
            return MapError(offset.Error);
        }

        var result = await _templateService.GetAllAsync(workspaceId, policyRefId, offset.Value, page.LimitOr(100), cancellationToken);
        return MapPage(result, offset.Value);
    }

    /// <summary>
    /// Creates a new message template.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="request">The template details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created template.</returns>
    [HttpPost]
    [RequiresPermission(PermissionNames.TemplatesManage)]
    public async Task<ActionResult<MessageTemplateResponse>> Create([FromWorkspace] int workspaceId, MessageTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _templateService.CreateAsync(workspaceId, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Updates an existing message template.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="refId">The unique reference ID of the template.</param>
    /// <param name="request">The new template details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPut("{refId:guid}")]
    [RequiresPermission(PermissionNames.TemplatesManage)]
    public async Task<IActionResult> Update([FromWorkspace] int workspaceId, Guid refId, MessageTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _templateService.UpdateAsync(workspaceId, refId, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Deletes a message template.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="refId">The unique reference ID of the template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpDelete("{refId:guid}")]
    [RequiresPermission(PermissionNames.TemplatesManage)]
    public async Task<IActionResult> Delete([FromWorkspace] int workspaceId, Guid refId, CancellationToken cancellationToken)
    {
        var result = await _templateService.DeleteAsync(workspaceId, refId, cancellationToken);
        return MapResult(result);
    }



}
