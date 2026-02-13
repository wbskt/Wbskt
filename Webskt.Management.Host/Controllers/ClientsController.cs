using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Common.Abstraction.Events;
using Webskt.Common.Abstraction.Events.Shared;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Common.Abstraction.Models;
using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Services;

namespace Webskt.Management.Host.Controllers;

[Route("api/clients")]
[ApiController]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly IReferenceMapper _policyMapper;
    private readonly IReferenceMapper _clientMapper;
    private readonly IEventBus _eventBus;

    public ClientsController(
        IClientService clientService, 
        [FromKeyedServices("RegistrationPolicy")] IReferenceMapper policyMapper,
        [FromKeyedServices("Client")] IReferenceMapper clientMapper,
        IEventBus eventBus)
    {
        _clientService = clientService;
        _policyMapper = policyMapper;
        _clientMapper = clientMapper;
        _eventBus = eventBus;
    }

    [HttpGet]
    public async Task<ListResponse<ClientResponse>> GetAll(
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var pagedData = await _clientService.GetAllAsync(status, name, skip, take, cancellationToken);
        
        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());

        return new ListResponse<ClientResponse>
        {
            Items = pagedData
        };
    }

    [HttpGet("policy/{policyRefId:guid}")]
    public async Task<ListResponse<ClientResponse>> GetByPolicy(
        Guid policyRefId,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var policyId = await _policyMapper.FindByReferenceIdAsync(policyRefId, cancellationToken);
        
        if (policyId <= 0)
        {
            throw new SecurityException($"Access denied for policy {policyRefId}.");
        }

        var pagedData = await _clientService.GetByPolicyIdAsync(policyId, status, name, skip, take, cancellationToken);

        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());

        return new ListResponse<ClientResponse>
        {
            Items = pagedData
        };
    }

    [HttpPatch("{clientRefId:guid}/status")]
    public async Task UpdateStatus(Guid clientRefId, UpdateClientStatusRequest request, CancellationToken cancellationToken)
    {
        var id = await _clientMapper.FindByReferenceIdAsync(clientRefId, cancellationToken);

        if (id <= 0)
        {
            throw new SecurityException($"Access denied for client {clientRefId}.");
        }

        await _clientService.UpdateStatusAsync(id, request.Status, cancellationToken);
    }

    [HttpPost("{clientRefId:guid}/command")]
    public async Task SendCommand(Guid clientRefId, DeviceCommandRequest request, CancellationToken cancellationToken)
    {
        // In a real scenario, we might verify if the client belongs to the user here
        await _eventBus.PublishAsync(new DeviceCommandEvent(clientRefId, request.Action, request.Payload), cancellationToken);
    }
}

public record UpdateClientStatusRequest(ClientStatus Status);

public record DeviceCommandRequest(string Action, object? Payload = null);