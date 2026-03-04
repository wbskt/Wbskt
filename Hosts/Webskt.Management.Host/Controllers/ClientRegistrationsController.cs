using Microsoft.AspNetCore.Mvc;
using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Services;

namespace Webskt.Management.Host.Controllers;

[Route("api/client-registrations")]
[ApiController]
public class ClientRegistrationsController : ControllerBase
{
    private readonly IClientRegistrationService _registrationService;

    public ClientRegistrationsController(IClientRegistrationService registrationService)
    {
        _registrationService = registrationService;
    }

    [HttpPost("initiate")]
    public async Task<ClientRegistrationResponse> Initiate(ClientRegistrationRequest request, CancellationToken cancellationToken)
    {
        return await _registrationService.InitiateRegistrationAsync(request, cancellationToken);
    }
}
