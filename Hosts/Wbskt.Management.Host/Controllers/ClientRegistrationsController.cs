using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Controllers;

// Pre-auth device flow (registration happens before a client has credentials), so this is
// deliberately anonymous.
[Route("api/client-registrations")]
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class ClientRegistrationsController : ApiControllerBase
{
    private readonly IClientRegistrationService _registrationService;
    private readonly ILogger<ClientRegistrationsController> _logger;

    public ClientRegistrationsController(IClientRegistrationService registrationService, ILogger<ClientRegistrationsController> logger)
    {
        _registrationService = registrationService;
        _logger = logger;
    }

    [HttpPost("initiate")]
    [EnableRateLimiting(RateLimitPolicies.DeviceRegistration)]
    public async Task<ActionResult<ClientRegistrationResponse>> Initiate(ClientRegistrationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Client Registration initiated for Client Name: '{ClientName}'", request.Name);
        var result = await _registrationService.InitiateRegistrationAsync(request, cancellationToken);
        return MapResult(result);
    }



}
