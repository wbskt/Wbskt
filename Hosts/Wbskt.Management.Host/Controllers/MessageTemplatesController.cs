using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
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
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<MessageTemplatesController> _logger;

    public MessageTemplatesController(
        IMessageTemplateService templateService,
        IAuthServiceClient authClient,
        ILogger<MessageTemplatesController> logger)
    {
        _templateService = templateService;
        _authClient = authClient;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the saved send-panel message templates for a workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="policyRefId">Optional filter: only templates pinned to this policy.</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of message templates.</returns>
    [HttpGet]
    public async Task<ActionResult<ListResponse<MessageTemplateResponse>>> GetAll(
        Guid workspaceRef,
        [FromQuery] Guid? policyRefId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetAll message templates requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.TemplatesRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ListResponse<MessageTemplateResponse>>.Failure(workspaceIdResult.Error));
        }

        var result = await _templateService.GetAllAsync(workspaceIdResult.Value, policyRefId, Paging.Skip(skip), Paging.Take(take), cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<MessageTemplateResponse>>.Failure(result.Error));
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());
        return Ok(new ListResponse<MessageTemplateResponse> { Items = result.Value });
    }

    /// <summary>
    /// Creates a new message template.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="request">The template details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created template.</returns>
    [HttpPost]
    public async Task<ActionResult<MessageTemplateResponse>> Create(Guid workspaceRef, MessageTemplateRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Create message template requested for WorkspaceRef: '{WorkspaceRef}' (Name: '{TemplateName}')", workspaceRef, request.Name);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.TemplatesManage, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<MessageTemplateResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _templateService.CreateAsync(workspaceIdResult.Value, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Updates an existing message template.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="refId">The unique reference ID of the template.</param>
    /// <param name="request">The new template details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPut("{refId:guid}")]
    public async Task<IActionResult> Update(Guid workspaceRef, Guid refId, MessageTemplateRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Update message template requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.TemplatesManage, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var result = await _templateService.UpdateAsync(workspaceIdResult.Value, refId, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Deletes a message template.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="refId">The unique reference ID of the template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpDelete("{refId:guid}")]
    public async Task<IActionResult> Delete(Guid workspaceRef, Guid refId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Delete message template requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.TemplatesManage, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var result = await _templateService.DeleteAsync(workspaceIdResult.Value, refId, cancellationToken);
        return MapResult(result);
    }



}
