using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Controllers;

// Pre-auth device flow (a client logging in has no JWT yet), so this is deliberately anonymous.
[Route("api/client-auth")]
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class ClientAuthController : ApiControllerBase
{
    private readonly IClientAuthService _authService;
    private readonly ILogger<ClientAuthController> _logger;

    public ClientAuthController(IClientAuthService authService, ILogger<ClientAuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.DeviceLogin)]
    public async Task<ActionResult<ClientLoginResponse>> Login(ClientLoginRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("API: Client Login requested for ClientRefId: {ClientRefId}", request.ClientRefId);
        var result = await _authService.LoginAsync(request, cancellationToken);
        return MapResult(result);
    }



}
