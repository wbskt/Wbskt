using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/clients")]
[ApiController]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly IReferenceMapper _clientMapper;
    private readonly IAuthServiceClient _authClient;
    private readonly IEventBus _eventBus;
    private readonly IReferenceMapper _policyMapper;
    private readonly IRegistrationPolicyService _policyService;

    public ClientsController(
        IClientService clientService,
        [FromKeyedServices(ReferenceType.Client)] IReferenceMapper clientMapper,
        IAuthServiceClient authClient, 
        IEventBus eventBus,
        [FromKeyedServices(ReferenceType.RegistrationPolicy)] IReferenceMapper policyMapper,
        IRegistrationPolicyService policyService)
    {
        _clientService = clientService;
        _clientMapper = clientMapper;
        _authClient = authClient;
        _eventBus = eventBus;
        _policyMapper = policyMapper;
        _policyService = policyService;
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
    public async Task<ListResponse<ClientResponse>> GetAll(
        Guid workspaceRef,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsRead, cancellationToken);
        
        var pagedData = await _clientService.GetAllAsync(workspaceId, status, name, skip, take, cancellationToken);
        
        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());

        return new ListResponse<ClientResponse>
        {
            Items = pagedData
        };
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
    /// <exception cref="SecurityException">Thrown if the policy reference is invalid or does not belong to the workspace.</exception>
    [HttpGet("policy/{policyRefId:guid}")]
    public async Task<ListResponse<ClientResponse>> GetByPolicy(
        Guid workspaceRef,
        Guid policyRefId,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        // 1. Authorize workspace access
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsRead, cancellationToken);

        // 2. Resolve Policy RefId
        var policyId = await _policyMapper.FindIdByRefIdAsync(policyRefId, cancellationToken);
        if (policyId <= 0)
        {
            throw new SecurityException("Access denied for policy.");
        }

        // 3. !! CRITICAL !! Verify Policy belongs to Workspace
        var policy = await _policyService.GetByIdAsync(policyId, cancellationToken);
        if (policy.WorkspaceId != workspaceId)
        {
            throw new SecurityException("Policy does not belong to the specified workspace.");
        }

        // 4. All checks pass, get the data
        var pagedData = await _clientService.GetByPolicyIdAsync(workspaceId, policyId, status, name, skip, take, cancellationToken);

        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());
        return new ListResponse<ClientResponse> { Items = pagedData };
    }

    /// <summary>
    /// Updates the status of a specific client (e.g., Revoking or Approving a client).
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the client to update.</param>
    /// <param name="request">The new status details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="SecurityException">Thrown if the client reference is invalid or access is denied.</exception>
    [HttpPatch("{clientRefId:guid}/status")]
    public async Task UpdateStatus(Guid workspaceRef, Guid clientRefId, UpdateClientStatusRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsUpdate, cancellationToken);
        var id = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);

        if (id <= 0)
        {
            throw new SecurityException($"Access denied for client {clientRefId}.");
        }

        await _clientService.UpdateStatusAsync(workspaceId, id, request.Status, cancellationToken);
    }

    /// <summary>
    /// Sends an asynchronous command payload to a specific registered client.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the target client.</param>
    /// <param name="request">The command name and payload data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{clientRefId:guid}/command")]
    public async Task SendCommand(Guid workspaceRef, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsCommand, cancellationToken);
        var clientId = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);
        await _eventBus.PublishAsync(new ClientCommandEvent(clientRefId, clientId, workspaceId, request.Type, request.Payload), cancellationToken);
    }

    /// <summary>
    /// Triggers a ping event for a specific client to verify connectivity or wake state.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the target client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{clientRefId:guid}/ping")]
    public async Task Ping(Guid workspaceRef, Guid clientRefId, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsPing, cancellationToken);
        var clientId = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);
        await _eventBus.PublishAsync(new ClientPingEvent(clientRefId, clientId, workspaceId, DateTime.UtcNow), cancellationToken);
    }
}

public record UpdateClientStatusRequest(ClientStatus Status);

public record ClientCommandRequest(string Type, string Payload);