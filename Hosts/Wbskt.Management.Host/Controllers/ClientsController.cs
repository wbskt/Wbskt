using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
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
    private readonly IReferenceMapper _clientMapper;
    private readonly IAuthServiceClient _authClient;
    private readonly IEventBus _eventBus;
    private readonly IReferenceMapper _policyMapper;
    private readonly IRegistrationPolicyService _policyService;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(
        IClientService clientService,
        [FromKeyedServices(ReferenceType.Client)] IReferenceMapper clientMapper,
        IAuthServiceClient authClient,
        IEventBus eventBus,
        [FromKeyedServices(ReferenceType.RegistrationPolicy)] IReferenceMapper policyMapper,
        IRegistrationPolicyService policyService,
        IEventLogService eventLogService,
        ILogger<ClientsController> logger)
    {
        _clientService = clientService;
        _clientMapper = clientMapper;
        _authClient = authClient;
        _eventBus = eventBus;
        _policyMapper = policyMapper;
        _policyService = policyService;
        _eventLogService = eventLogService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all clients for a specific workspace with optional filtering.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="status">Optional filter by client status (Pending, Registered, etc.).</param>
    /// <param name="name">Optional filter by client name (partial match).</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of clients.</returns>
    [HttpGet]
    public async Task<ActionResult<ListResponse<ClientResponse>>> GetAll(
        Guid workspaceRef,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetAll requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);
        
        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(workspaceIdResult.Error));
        }

        var result = await _clientService.GetAllAsync(workspaceIdResult.Value, status, name, skip, take, cancellationToken);
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
    /// Retrieves all clients associated with a specific registration policy.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="policyRefId">The unique reference ID of the registration policy.</param>
    /// <param name="status">Optional filter by client status.</param>
    /// <param name="name">Optional filter by client name.</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of clients linked to the specified policy.</returns>
    [HttpGet("policy/{policyRefId:guid}")]
    public async Task<ActionResult<ListResponse<ClientResponse>>> GetByPolicy(
        Guid workspaceRef,
        Guid policyRefId,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetByPolicy requested for WorkspaceRef: '{WorkspaceRef}', PolicyRefId: '{PolicyRefId}'", workspaceRef, policyRefId);

        // 1. Authorize workspace access
        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(workspaceIdResult.Error));
        }

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

        if (policyResult.Value.WorkspaceId != workspaceIdResult.Value)
        {
            _logger.LogWarning("Access denied: Policy ID {PolicyId} does not belong to Workspace ID {WorkspaceId}", policyId, workspaceIdResult.Value);
            return MapError(Error.Forbidden("POLICY_UNAUTHORIZED", "Policy does not belong to the specified workspace."));
        }

        // 4. All checks pass, get the data
        var result = await _clientService.GetByPolicyIdAsync(workspaceIdResult.Value, policyId, status, name, skip, take, cancellationToken);
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
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The client detail.</returns>
    [HttpGet("{clientRefId:guid}")]
    public async Task<ActionResult<ClientDetailResponse>> GetDetail(Guid workspaceRef, Guid clientRefId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetDetail requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ClientDetailResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _clientService.GetDetailAsync(workspaceIdResult.Value, clientRefId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Updates the status of a specific client (e.g., Revoking or Approving a client).
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the client to update.</param>
    /// <param name="request">The new status details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPatch("{clientRefId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid workspaceRef, Guid clientRefId, UpdateClientStatusRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: UpdateStatus requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}' to Status: '{Status}'", workspaceRef, clientRefId, request.Status);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsUpdate, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var id = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);
        if (id <= 0)
        {
            return NotFound(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
        }

        var result = await _clientService.UpdateStatusAsync(workspaceIdResult.Value, id, request.Status, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Applies one status to many clients: approving or revoking a batch of pending devices at once.
    /// Each client is handled as the single-client endpoint would handle it, so one that fails (the
    /// policy is full, or the client is not in this workspace) is reported and the rest still change.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="request">The clients (at most 100) and the status to give them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Which clients changed and which did not, with why.</returns>
    [HttpPatch("status")]
    public async Task<ActionResult<BulkClientStatusResponse>> UpdateStatuses(Guid workspaceRef, BulkClientStatusRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: UpdateStatuses requested for WorkspaceRef: '{WorkspaceRef}' ({Count} clients) to Status: '{Status}'", workspaceRef, request.ClientRefIds?.Count ?? 0, request.Status);

        if (request.ClientRefIds is null || request.ClientRefIds.Count == 0 || request.ClientRefIds.Count > BulkClientStatusRequest.MaxClients)
        {
            return BadRequest(Error.Validation("CLIENT_REFS_INVALID", $"Name between 1 and {BulkClientStatusRequest.MaxClients} clients."));
        }

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsUpdate, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<BulkClientStatusResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _clientService.UpdateStatusesAsync(workspaceIdResult.Value, request.ClientRefIds, request.Status, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Deletes a client. Its connection is closed, its token refused, and its capabilities and
    /// state removed; its event-log history stays. The device has to register again to come back.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpDelete("{clientRefId:guid}")]
    public async Task<IActionResult> Delete(Guid workspaceRef, Guid clientRefId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Delete requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsManage, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var result = await _clientService.DeleteAsync(workspaceIdResult.Value, clientRefId, cancellationToken);
        return result.IsSuccess ? NoContent() : MapError(result.Error);
    }

    /// <summary>
    /// Replaces a client's secret and returns the new one, once. The old secret stops working at
    /// once and the live connection is closed; the device reconnects when it is given the new one.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new secret.</returns>
    [HttpPost("{clientRefId:guid}/rotate-secret")]
    public async Task<ActionResult<ClientSecretResponse>> RotateSecret(Guid workspaceRef, Guid clientRefId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RotateSecret requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsManage, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ClientSecretResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _clientService.RotateSecretAsync(workspaceIdResult.Value, clientRefId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Retrieves the last-known state variables self-reported by a specific client.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The client's state variables.</returns>
    [HttpGet("{clientRefId:guid}/state")]
    public async Task<ActionResult<ListResponse<ClientStateVariableResponse>>> GetState(Guid workspaceRef, Guid clientRefId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetState requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientStateVariableResponse>>.Failure(workspaceIdResult.Error));
        }

        var id = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);
        if (id <= 0)
        {
            return NotFound(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
        }

        var result = await _clientService.GetStateAsync(workspaceIdResult.Value, id, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientStateVariableResponse>>.Failure(result.Error));
        }

        return Ok(new ListResponse<ClientStateVariableResponse> { Items = result.Value });
    }

    /// <summary>
    /// Renames a specific client.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the client to rename.</param>
    /// <param name="request">The new name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPatch("{clientRefId:guid}/name")]
    public async Task<IActionResult> Rename(Guid workspaceRef, Guid clientRefId, UpdateClientNameRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Rename requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
        {
            return BadRequest(Error.Validation("CLIENT_NAME_INVALID", "Client name must be 1-100 characters."));
        }

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsUpdate, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var id = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);
        if (id <= 0)
        {
            return NotFound(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
        }

        var result = await _clientService.RenameAsync(workspaceIdResult.Value, id, request.Name.Trim(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Sends an asynchronous command payload to a specific registered client.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the target client.</param>
    /// <param name="request">The command name and payload data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The command id, for correlating the delivery/ack events that follow.</returns>
    [HttpPost("{clientRefId:guid}/command")]
    public async Task<IActionResult> SendCommand(Guid workspaceRef, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: SendCommand requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

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

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsCommand, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        // Ownership has to be settled here: the command goes out over the bus and the socket host
        // routes it by ClientRefId alone, so nothing downstream would notice a client from another
        // workspace.
        var clientResult = await _clientService.EnsureClientInWorkspaceAsync(workspaceIdResult.Value, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return MapError(clientResult.Error);
        }

        var commandId = Guid.NewGuid();
        if (!await TryPublishActionAsync(new ClientCommandEvent(clientRefId, clientResult.Value, workspaceIdResult.Value, request.Type, request.Payload, commandId), cancellationToken))
        {
            return BrokerUnavailable();
        }

        _logger.LogInformation("Successfully published client command event '{CommandId}' for ClientRefId: '{ClientRefId}'", commandId, clientRefId);
        return Accepted(new ClientCommandResponse(commandId));
    }

    /// <summary>
    /// Retrieves the recent in/out communication history for a specific client
    /// (backfill for the Live Comms panel before the realtime stream attaches).
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the client.</param>
    /// <param name="direction">Optional direction filter: "in" or "out". Omit for both (plus connect/disconnect rows).</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of comms event-log entries, newest first.</returns>
    [HttpGet("{clientRefId:guid}/comms")]
    public async Task<ActionResult<ListResponse<EventLogResponse>>> GetComms(
        Guid workspaceRef,
        Guid clientRefId,
        [FromQuery] string? direction,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetComms requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

        if (direction is not (null or "in" or "out"))
        {
            return BadRequest(Error.Validation("DIRECTION_INVALID", "Direction must be 'in', 'out', or omitted."));
        }

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.LogsRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ListResponse<EventLogResponse>>.Failure(workspaceIdResult.Error));
        }

        // The query is workspace-filtered in SQL, so a foreign client would come back as an empty
        // page. Resolving ownership up front reports it as the 403 the sibling endpoints return,
        // rather than as a client that exists but has never said anything.
        var clientResult = await _clientService.EnsureClientInWorkspaceAsync(workspaceIdResult.Value, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return MapResult(Result<ListResponse<EventLogResponse>>.Failure(clientResult.Error));
        }

        var result = await _eventLogService.GetClientCommsAsync(workspaceIdResult.Value, clientResult.Value, direction, skip, take, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<EventLogResponse>>.Failure(result.Error));
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());
        return Ok(new ListResponse<EventLogResponse> { Items = result.Value });
    }

    /// <summary>
    /// Triggers a ping event for a specific client to verify connectivity or wake state.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the target client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{clientRefId:guid}/ping")]
    public async Task<IActionResult> Ping(Guid workspaceRef, Guid clientRefId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Ping requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsPing, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        // Same reasoning as SendCommand: the ping is dispatched by ClientRefId, so the workspace it
        // belongs to is only ever checked here.
        var clientResult = await _clientService.EnsureClientInWorkspaceAsync(workspaceIdResult.Value, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return MapError(clientResult.Error);
        }

        if (!await TryPublishActionAsync(new ClientPingEvent(clientRefId, clientResult.Value, workspaceIdResult.Value, DateTime.UtcNow), cancellationToken))
        {
            return BrokerUnavailable();
        }

        _logger.LogInformation("Successfully published client ping event for ClientRefId: '{ClientRefId}'", clientRefId);
        return NoContent();
    }

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

public record ClientCommandRequest(string Type, string Payload);

public record ClientCommandResponse(Guid CommandId);