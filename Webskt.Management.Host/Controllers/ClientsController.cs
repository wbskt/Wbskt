using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Management.Host.Models;
using Webskt.Management.Host.Services;

namespace Webskt.Management.Host.Controllers;

[Route("api/clients")]
[ApiController]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly IRegistrationPolicyService _policyService;

    public ClientsController(IClientService clientService, IRegistrationPolicyService policyService)
    {
        _clientService = clientService;
        _policyService = policyService;
    }

    [HttpGet]
    public async Task<IReadOnlyCollection<ClientResponse>> GetAll()
    {
        return await _clientService.GetAllAsync();
    }

    [HttpGet("policy/{policyRefId:guid}")]
    public async Task<IReadOnlyCollection<ClientResponse>> GetByPolicy(Guid policyRefId)
    {
        var policyId = await _policyService.FindByRefIdAsync(policyRefId);
        return await _clientService.GetByPolicyIdAsync(policyId);
    }
}
