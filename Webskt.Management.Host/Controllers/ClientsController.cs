using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Common.Abstraction.Events;
using Webskt.Common.Abstraction.Events.Shared;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Interfaces;
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
    private readonly IEventBus _eventBus;

    public ClientsController(
        IClientService clientService, 
        [FromKeyedServices("RegistrationPolicy")] IReferenceMapper policyMapper,
        IEventBus eventBus)
    {
        _clientService = clientService;
        _policyMapper = policyMapper;
        _eventBus = eventBus;
    }

    [HttpGet]
    public async Task<IReadOnlyCollection<ClientResponse>> GetAll()
    {
        return await _clientService.GetAllAsync();
    }

    [HttpGet("policy/{policyRefId:guid}")]
    public async Task<IReadOnlyCollection<ClientResponse>> GetByPolicy(Guid policyRefId)
    {
        var policyId = await _policyMapper.FindByReferenceIdAsync(policyRefId);
        
        if (policyId <= 0)
        {
            throw new SecurityException($"Access denied for policy {policyRefId}.");
        }

        return await _clientService.GetByPolicyIdAsync(policyId);
    }

    [HttpPost("{clientRefId:guid}/command")]
    public async Task SendCommand(Guid clientRefId, DeviceCommandRequest request)
    {
        // In a real scenario, we might verify if the client belongs to the user here
        await _eventBus.PublishAsync(new DeviceCommandEvent(clientRefId, request.Action, request.Payload));
    }
}

public record DeviceCommandRequest(string Action, object? Payload = null);