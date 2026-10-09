using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/clients")]
[ApiController]
[Authorize]
public class ClientsController : ApiControllerBase
{
    private readonly IClientService _clientService;
    private readonly IClientCommandService _commandService;
    private readonly IRegistrationPolicyService _policyService;
    private readonly IEventLogService _eventLogService;

    public ClientsController(
        IClientService clientService,
        IClientCommandService commandService,
        IRegistrationPolicyService policyService,
        IEventLogService eventLogService)
    {
        _clientService = clientService;
        _commandService = commandService;
        _policyService = policyService;
        _eventLogService = eventLogService;
    }

    /// <summary>
    /// Retrieves all clients for a specific workspace with optional filtering.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="status">Optional filter by client status (Pending, Registered, etc.).</param>
    /// <param name="name">Optional filter by client name (partial match).</param>
    /// <param name="tag">Optional filter: only clients carrying this tag (case-insensitive).</param>
    /// <param name="page">The page to read: <c>cursor</c> and <c>limit</c> (default 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of clients, with the total count.</returns>
    [HttpGet]
    [RequiresPermission(PermissionNames.ClientsRead)]
    public async Task<ActionResult<Page<ClientResponse>>> GetAll(
        [FromWorkspace] int workspaceId,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] string? tag,
        [FromQuery] PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var offset = page.Offset();
        if (offset.IsFailure)
        {
            return MapError(offset.Error);
        }

        var result = await _clientService.GetAllAsync(workspaceId, status, name, tag, offset.Value, page.LimitOr(100), cancellationToken);
        return MapPage(result, offset.Value);
    }

    /// <summary>
    /// Lists every tag in use in the workspace with how many clients carry it, for a tag filter.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tags, sorted.</returns>
    [HttpGet("tags")]
    [RequiresPermission(PermissionNames.ClientsRead)]
    public async Task<ActionResult<Page<ClientTagCountResponse>>> GetTags([FromWorkspace] int workspaceId, CancellationToken cancellationToken)
    {
        var result = await _clientService.GetTagsAsync(workspaceId, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<Page<ClientTagCountResponse>>.Failure(result.Error));
        }

        return Ok(new Page<ClientTagCountResponse> { Items = result.Value });
    }

    /// <summary>
    /// Retrieves all clients associated with a specific registration policy.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="policyRefId">The unique reference ID of the registration policy.</param>
    /// <param name="status">Optional filter by client status.</param>
    /// <param name="name">Optional filter by client name.</param>
    /// <param name="tag">Optional filter: only clients carrying this tag (case-insensitive).</param>
    /// <param name="page">The page to read: <c>cursor</c> and <c>limit</c> (default 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of the clients linked to the policy, with the total count.</returns>
    [HttpGet("policy/{policyRefId:guid}")]
    [RequiresPermission(PermissionNames.ClientsRead)]
    public async Task<ActionResult<Page<ClientResponse>>> GetByPolicy(
        [FromWorkspace] int workspaceId,
        Guid policyRefId,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] string? tag,
        [FromQuery] PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var offset = page.Offset();
        if (offset.IsFailure)
        {
            return MapError(offset.Error);
        }

        // Another workspace's policy is as unknown here as one that does not exist, not an empty page.
        var policy = await _policyService.FindInWorkspaceAsync(workspaceId, policyRefId, cancellationToken);
        if (policy.IsFailure)
        {
            return MapError(policy.Error);
        }

        var result = await _clientService.GetByPolicyIdAsync(workspaceId, policy.Value.Id, status, name, tag, offset.Value, page.LimitOr(100), cancellationToken);
        return MapPage(result, offset.Value);
    }

    /// <summary>
    /// Retrieves the full detail of a single client: presence, uptime anchor, latency,
    /// self-reported SDK metadata and command capabilities.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The client detail.</returns>
    [HttpGet("{clientRefId:guid}")]
    [RequiresPermission(PermissionNames.ClientsRead)]
    public async Task<ActionResult<ClientDetailResponse>> GetDetail([FromWorkspace] int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        var result = await _clientService.GetDetailAsync(workspaceId, clientRefId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Updates the status of a specific client (e.g., Revoking or Approving a client).
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the client to update.</param>
    /// <param name="request">The new status details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPatch("{clientRefId:guid}/status")]
    [RequiresPermission(PermissionNames.ClientsUpdate)]
    public async Task<IActionResult> UpdateStatus([FromWorkspace] int workspaceId, Guid clientRefId, UpdateClientStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _clientService.UpdateStatusAsync(workspaceId, clientRefId, request.Status, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Applies one status to many clients: approving or revoking a batch of pending devices at once.
    /// Each client is handled as the single-client endpoint would handle it, so one that fails (the
    /// policy is full, or the client is not in this workspace) is reported and the rest still change.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="request">The clients (at most 100) and the status to give them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Which clients changed and which did not, with why.</returns>
    [HttpPatch("status")]
    [RequiresPermission(PermissionNames.ClientsUpdate)]
    public async Task<ActionResult<BulkClientStatusResponse>> UpdateStatuses([FromWorkspace] int workspaceId, BulkClientStatusRequest request, CancellationToken cancellationToken)
    {
        if (request.ClientRefIds is null || request.ClientRefIds.Count == 0 || request.ClientRefIds.Count > BulkClientStatusRequest.MaxClients)
        {
            return BadRequest(Error.Validation("CLIENT_REFS_INVALID", $"Name between 1 and {BulkClientStatusRequest.MaxClients} clients."));
        }

        var result = await _clientService.UpdateStatusesAsync(workspaceId, request.ClientRefIds, request.Status, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Deletes a client. Its connection is closed, its token refused, and its capabilities and
    /// state removed; its event-log history stays. The device has to register again to come back.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpDelete("{clientRefId:guid}")]
    [RequiresPermission(PermissionNames.ClientsManage)]
    public async Task<IActionResult> Delete([FromWorkspace] int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        var result = await _clientService.DeleteAsync(workspaceId, clientRefId, cancellationToken);
        return result.IsSuccess ? NoContent() : MapError(result.Error);
    }

    /// <summary>
    /// Replaces a client's secret and returns the new one, once. The old secret stops working at
    /// once and the live connection is closed; the device reconnects when it is given the new one.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new secret.</returns>
    [HttpPost("{clientRefId:guid}/rotate-secret")]
    [RequiresPermission(PermissionNames.ClientsManage)]
    public async Task<ActionResult<ClientSecretResponse>> RotateSecret([FromWorkspace] int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        var result = await _clientService.RotateSecretAsync(workspaceId, clientRefId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Retrieves the last-known state variables self-reported by a specific client.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The client's state variables.</returns>
    [HttpGet("{clientRefId:guid}/state")]
    [RequiresPermission(PermissionNames.ClientsRead)]
    public async Task<ActionResult<Page<ClientStateVariableResponse>>> GetState([FromWorkspace] int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        var result = await _clientService.GetStateAsync(workspaceId, clientRefId, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<Page<ClientStateVariableResponse>>.Failure(result.Error));
        }

        return Ok(new Page<ClientStateVariableResponse> { Items = result.Value });
    }

    /// <summary>
    /// Renames a specific client.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the client to rename.</param>
    /// <param name="request">The new name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPatch("{clientRefId:guid}/name")]
    [RequiresPermission(PermissionNames.ClientsUpdate)]
    public async Task<IActionResult> Rename([FromWorkspace] int workspaceId, Guid clientRefId, UpdateClientNameRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
        {
            return BadRequest(Error.Validation("CLIENT_NAME_INVALID", "Client name must be 1-100 characters."));
        }

        var result = await _clientService.RenameAsync(workspaceId, clientRefId, request.Name.Trim(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Replaces a client's tags. Tags are trimmed and lower-cased, duplicates collapse, and an empty
    /// list clears them.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="request">The client's tags, at most 10.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tags as stored, sorted.</returns>
    [HttpPut("{clientRefId:guid}/tags")]
    [RequiresPermission(PermissionNames.ClientsUpdate)]
    public async Task<ActionResult<ClientTagsResponse>> SetTags([FromWorkspace] int workspaceId, Guid clientRefId, SetClientTagsRequest request, CancellationToken cancellationToken)
    {
        var result = await _clientService.SetTagsAsync(workspaceId, clientRefId, request.Tags, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Sends an asynchronous command payload to a specific registered client.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the target client.</param>
    /// <param name="request">The command name and payload data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The command id, for correlating the delivery/ack events that follow.</returns>
    [HttpPost("{clientRefId:guid}/command")]
    [RequiresPermission(PermissionNames.ClientsCommand)]
    public async Task<ActionResult<ClientCommandResponse>> SendCommand([FromWorkspace] int workspaceId, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken)
    {
        var result = await _commandService.SendAsync(workspaceId, clientRefId, request, cancellationToken);
        return result.IsSuccess ? Accepted(result.Value) : MapError(result.Error);
    }

    /// <summary>
    /// Retrieves the recent in/out communication history for a specific client
    /// (backfill for the Live Comms panel before the realtime stream attaches).
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="direction">Optional direction filter: "in" or "out". Omit for both (plus connect/disconnect rows).</param>
    /// <param name="page">The page to read: <c>cursor</c> (left out for the newest entries) and <c>limit</c> (default 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of comms event-log entries, newest first, with the cursor for the next page.</returns>
    [HttpGet("{clientRefId:guid}/comms")]
    [RequiresPermission(PermissionNames.LogsRead)]
    public async Task<ActionResult<Page<EventLogResponse>>> GetComms(
        [FromWorkspace] int workspaceId,
        Guid clientRefId,
        [FromQuery] string? direction,
        [FromQuery] PageRequest page,
        CancellationToken cancellationToken = default)
    {
        if (direction is not (null or "in" or "out"))
        {
            return BadRequest(Error.Validation("DIRECTION_INVALID", "Direction must be 'in', 'out', or omitted."));
        }

        var cursor = page.AfterKey();
        if (cursor.IsFailure)
        {
            return MapError(cursor.Error);
        }

        // The query is workspace-filtered in SQL, so a foreign client would come back as an empty
        // page. Resolving ownership up front reports it as the 403 the sibling endpoints return,
        // rather than as a client that exists but has never said anything.
        var clientResult = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return MapResult(Result<Page<EventLogResponse>>.Failure(clientResult.Error));
        }

        return MapResult(await _eventLogService.GetClientCommsAsync(workspaceId, clientResult.Value, direction, cursor.Value, page.LimitOr(50), cancellationToken));
    }

    /// <summary>
    /// Triggers a ping event for a specific client to verify connectivity or wake state.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the target client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{clientRefId:guid}/ping")]
    [RequiresPermission(PermissionNames.ClientsPing)]
    public async Task<IActionResult> Ping([FromWorkspace] int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        return MapResult(await _commandService.PingAsync(workspaceId, clientRefId, cancellationToken));
    }
}

public record UpdateClientStatusRequest(ClientStatus Status);

public record UpdateClientNameRequest(string Name);

public record SetClientTagsRequest(IReadOnlyList<string>? Tags);
