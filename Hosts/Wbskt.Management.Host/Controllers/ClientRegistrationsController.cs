using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Controllers;

[Route("api/client-registrations")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
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
