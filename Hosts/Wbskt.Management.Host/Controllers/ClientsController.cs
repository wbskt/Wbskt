using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
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
    private readonly IEventBus _eventBus;
    private readonly IReferenceMapper _policyMapper;
    private readonly IRegistrationPolicyService _policyService;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(
        IClientService clientService,
        IEventBus eventBus,
        [FromKeyedServices(ReferenceType.RegistrationPolicy)] IReferenceMapper policyMapper,
        IRegistrationPolicyService policyService,
        IEventLogService eventLogService,
        ILogger<ClientsController> logger)
    {
        _clientService = clientService;
        _eventBus = eventBus;
        _policyMapper = policyMapper;
        _policyService = policyService;
        _eventLogService = eventLogService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all clients for a specific workspace with optional filtering.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="status">Optional filter by client status (Pending, Registered, etc.).</param>
    /// <param name="name">Optional filter by client name (partial match).</param>
    /// <param name="tag">Optional filter: only clients carrying this tag (case-insensitive).</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of clients.</returns>
    [HttpGet]
    [RequiresPermission(PermissionNames.ClientsRead)]
    public async Task<ActionResult<ListResponse<ClientResponse>>> GetAll(
        [FromWorkspace] int workspaceId,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] string? tag,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _clientService.GetAllAsync(workspaceId, status, name, tag, Paging.Skip(skip), Paging.Take(take), cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(result.Error));
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());

        return Ok(new ListResponse<ClientResponse>
        {
            Items = result.Value
        });
    }

    /// <summary>
    /// Lists every tag in use in the workspace with how many clients carry it, for a tag filter.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tags, sorted.</returns>
    [HttpGet("tags")]
    [RequiresPermission(PermissionNames.ClientsRead)]
    public async Task<ActionResult<ListResponse<ClientTagCountResponse>>> GetTags([FromWorkspace] int workspaceId, CancellationToken cancellationToken)
    {
        var result = await _clientService.GetTagsAsync(workspaceId, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientTagCountResponse>>.Failure(result.Error));
        }

        return Ok(new ListResponse<ClientTagCountResponse> { Items = result.Value });
    }

    /// <summary>
    /// Retrieves all clients associated with a specific registration policy.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="policyRefId">The unique reference ID of the registration policy.</param>
    /// <param name="status">Optional filter by client status.</param>
    /// <param name="name">Optional filter by client name.</param>
    /// <param name="tag">Optional filter: only clients carrying this tag (case-insensitive).</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of clients linked to the specified policy.</returns>
    [HttpGet("policy/{policyRefId:guid}")]
    [RequiresPermission(PermissionNames.ClientsRead)]
    public async Task<ActionResult<ListResponse<ClientResponse>>> GetByPolicy(
        [FromWorkspace] int workspaceId,
        Guid policyRefId,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] string? tag,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        // 2. Resolve Policy RefId
        var policyId = await _policyMapper.FindIdByRefIdAsync(policyRefId, cancellationToken);
        if (policyId <= 0)
        {
            return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }

        // 3. Verify Policy belongs to Workspace
        var policyResult = await _policyService.GetByIdAsync(policyId, cancellationToken);
        if (policyResult.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(policyResult.Error));
        }

        if (policyResult.Value.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Access denied: Policy ID {PolicyId} does not belong to Workspace ID {WorkspaceId}", policyId, workspaceId);
            return MapError(Error.Forbidden("POLICY_UNAUTHORIZED", "Policy does not belong to the specified workspace."));
        }

        // 4. All checks pass, get the data
        var result = await _clientService.GetByPolicyIdAsync(workspaceId, policyId, status, name, tag, Paging.Skip(skip), Paging.Take(take), cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(result.Error));
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());
        return Ok(new ListResponse<ClientResponse> { Items = result.Value });
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
    public async Task<ActionResult<ListResponse<ClientStateVariableResponse>>> GetState([FromWorkspace] int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        var result = await _clientService.GetStateAsync(workspaceId, clientRefId, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientStateVariableResponse>>.Failure(result.Error));
        }

        return Ok(new ListResponse<ClientStateVariableResponse> { Items = result.Value });
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
    public async Task<IActionResult> SendCommand([FromWorkspace] int workspaceId, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > 100)
        {
            return BadRequest(Error.Validation("COMMAND_TYPE_INVALID", "Command type must be 1-100 characters."));
        }

        if (ReservedMessageTypes.IsReserved(request.Type))
        {
            return BadRequest(Error.Validation("COMMAND_TYPE_RESERVED", "Command type is reserved for the platform protocol."));
        }

        if (request.Payload is { Length: > 32 * 1024 })
        {
            return BadRequest(Error.Validation("COMMAND_PAYLOAD_TOO_LARGE", "Command payload is limited to 32768 characters."));
        }

        if (request.ExpiresAt is { } expiresAt
            && (expiresAt <= DateTimeOffset.UtcNow || expiresAt > DateTimeOffset.UtcNow.Add(MaxCommandLifetime)))
        {
            return BadRequest(Error.Validation("COMMAND_EXPIRY_INVALID", "expiresAt must be in the future and at most 24 hours away."));
        }

        // Ownership has to be settled here: the command goes out over the bus and the socket host
        // routes it by ClientRefId alone, so nothing downstream would notice a client from another
        // workspace. Presence too: commands are delivered live or not at all, so an offline device
        // is a 409 DEVICE_OFFLINE now rather than a 202 for a command that goes nowhere.
        var targetResult = await _clientService.ResolveCommandTargetAsync(workspaceId, clientRefId, cancellationToken);
        if (targetResult.IsFailure)
        {
            return MapError(targetResult.Error);
        }

        var target = targetResult.Value;
        var commandId = Guid.NewGuid();
        var command = new ClientCommandEvent(clientRefId, target.ClientId, workspaceId, request.Type, request.Payload, commandId,
            TargetHostId: target.HostId, ExpiresAtUtc: request.ExpiresAt?.UtcDateTime);
        if (!await TryPublishActionAsync(command, cancellationToken))
        {
            return BrokerUnavailable();
        }

        _logger.LogDebug("Successfully published client command event '{CommandId}' for ClientRefId: '{ClientRefId}'", commandId, clientRefId);
        return Accepted(new ClientCommandResponse(commandId));
    }

    /// <summary>
    /// Retrieves the recent in/out communication history for a specific client
    /// (backfill for the Live Comms panel before the realtime stream attaches).
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="direction">Optional direction filter: "in" or "out". Omit for both (plus connect/disconnect rows).</param>
    /// <param name="cursor">The <c>nextCursor</c> of the previous page; omit for the newest entries.</param>
    /// <param name="take">Page size, 1 to 200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of comms event-log entries, newest first, with the cursor for the next page.</returns>
    [HttpGet("{clientRefId:guid}/comms")]
    [RequiresPermission(PermissionNames.LogsRead)]
    public async Task<ActionResult<EventLogListResponse>> GetComms(
        [FromWorkspace] int workspaceId,
        Guid clientRefId,
        [FromQuery] string? direction,
        [FromQuery] long? cursor = null,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (direction is not (null or "in" or "out"))
        {
            return BadRequest(Error.Validation("DIRECTION_INVALID", "Direction must be 'in', 'out', or omitted."));
        }

        // The query is workspace-filtered in SQL, so a foreign client would come back as an empty
        // page. Resolving ownership up front reports it as the 403 the sibling endpoints return,
        // rather than as a client that exists but has never said anything.
        var clientResult = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return MapResult(Result<EventLogListResponse>.Failure(clientResult.Error));
        }

        return MapResult(await _eventLogService.GetClientCommsAsync(workspaceId, clientResult.Value, direction, cursor, take, cancellationToken));
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
        // Same reasoning as SendCommand: the ping is dispatched by ClientRefId, so the workspace it
        // belongs to is only ever checked here.
        var clientResult = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return MapError(clientResult.Error);
        }

        if (!await TryPublishActionAsync(new ClientPingEvent(clientRefId, clientResult.Value, workspaceId, DateTime.UtcNow), cancellationToken))
        {
            return BrokerUnavailable();
        }

        _logger.LogDebug("Successfully published client ping event for ClientRefId: '{ClientRefId}'", clientRefId);
        return NoContent();
    }

    /// <summary>The furthest ahead a command's expiresAt may be.</summary>
    private static readonly TimeSpan MaxCommandLifetime = TimeSpan.FromHours(24);

    /// <summary>How long a command or ping waits for the broker before the caller is told to retry.</summary>
    internal static readonly TimeSpan ActionPublishTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Publishes an event that is itself the action - a command or a ping - on the real bus. Unlike the
    /// services' post-commit events these are not queued: a queued command would be reported as sent
    /// when it might never go out. So a broker that fails or does not answer in time is reported as
    /// such, rather than as a 500 or a hung request.
    /// </summary>
    private async Task<bool> TryPublishActionAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : IEvent
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ActionPublishTimeout);

        try
        {
            await _eventBus.PublishAsync(@event, timeout.Token);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Publishing {EventType} failed; the event bus is unavailable. {Message}", typeof(TEvent).Name, ex.Message);
            return false;
        }
    }

    private ObjectResult BrokerUnavailable()
    {
        Response.Headers.RetryAfter = "5";
        return StatusCode(StatusCodes.Status503ServiceUnavailable, Error.Failure("EVENT_BUS_UNAVAILABLE", "The device could not be reached right now. Try again shortly."));
    }
}

public record UpdateClientStatusRequest(ClientStatus Status);

public record UpdateClientNameRequest(string Name);

public record SetClientTagsRequest(IReadOnlyList<string>? Tags);

// ExpiresAt is optional: past it the command is refused instead of delivered, by the socket host
// and by the SDK. At most 24 hours ahead.
public record ClientCommandRequest(string Type, string Payload, DateTimeOffset? ExpiresAt = null);

public record ClientCommandResponse(Guid CommandId);