using Webskt.Common.Abstraction.Models.Management;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Management.Host.Models;
using Webskt.Management.Host.Services;

namespace Webskt.Management.Host.Controllers;

[Route("api/clients")]
[ApiController]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly IReferenceMapper _policyMapper;

    public ClientsController(
        IClientService clientService, 
        [FromKeyedServices("RegistrationPolicy")] IReferenceMapper policyMapper)
    {
        _clientService = clientService;
        _policyMapper = policyMapper;
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
}
