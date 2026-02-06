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

    public ClientsController(IClientService clientService)
    {
        _clientService = clientService;
    }

    [HttpGet]
    public async Task<IReadOnlyCollection<ClientResponse>> GetAll()
    {
        return await _clientService.GetAllAsync();
    }

    [HttpGet("policy/{policyRefId:guid}")]
    public async Task<IReadOnlyCollection<ClientResponse>> GetByPolicy(Guid policyRefId)
    {
        return await _clientService.GetByPolicyRefIdAsync(policyRefId);
    }
}
