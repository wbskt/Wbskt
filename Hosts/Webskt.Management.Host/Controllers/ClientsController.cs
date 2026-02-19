using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Common.Abstraction.Models;
using Webskt.Common.Abstraction.Models.Management;
using Webskt.EventBus.Abstractions;
using Webskt.Events.Shared;
using Webskt.Management.Host.Services;
using Webskt.Management.Host.Services.Clients;

namespace Webskt.Management.Host.Controllers;

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
        [FromKeyedServices("Client")] IReferenceMapper clientMapper,
        IAuthServiceClient authClient, 
        IEventBus eventBus,
        [FromKeyedServices("RegistrationPolicy")] IReferenceMapper policyMapper,
        IRegistrationPolicyService policyService)
    {
        _clientService = clientService;
        _clientMapper = clientMapper;
        _authClient = authClient;
        _eventBus = eventBus;
        _policyMapper = policyMapper;
        _policyService = policyService;
    }

    [HttpGet]
    public async Task<ListResponse<ClientResponse>> GetAll(
        Guid workspaceRef,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        await _authClient.ResolveWorkspaceAsync(workspaceRef, "clients.read", cancellationToken);
        
        var pagedData = await _clientService.GetAllAsync(status, name, skip, take, cancellationToken);
        
        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());

        return new ListResponse<ClientResponse>
        {
            Items = pagedData
        };
    }

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
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "clients.read", cancellationToken);

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
        var pagedData = await _clientService.GetByPolicyIdAsync(policyId, status, name, skip, take, cancellationToken);

        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());
        return new ListResponse<ClientResponse> { Items = pagedData };
    }

    [HttpPatch("{clientRefId:guid}/status")]
    public async Task UpdateStatus(Guid workspaceRef, Guid clientRefId, UpdateClientStatusRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "clients.update", cancellationToken);
        var id = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);

        if (id <= 0)
        {
            throw new SecurityException($"Access denied for client {clientRefId}.");
        }

        await _clientService.UpdateStatusAsync(workspaceId, id, request.Status, cancellationToken);
    }

    [HttpPost("{clientRefId:guid}/command")]
    public async Task SendCommand(Guid workspaceRef, Guid clientRefId, DeviceCommandRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "clients.command", cancellationToken);
        await _eventBus.PublishAsync(new DeviceCommandEvent(clientRefId, request.Action, workspaceId, request.Payload), cancellationToken);
    }
}

public record UpdateClientStatusRequest(ClientStatus Status);

public record DeviceCommandRequest(string Action, object? Payload = null);