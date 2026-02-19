using Webskt.Common.Abstraction.Models.Management;
using Microsoft.AspNetCore.Mvc;
using Webskt.Management.Host.Services;

namespace Webskt.Management.Host.Controllers;

[Route("api/client-auth")]
[ApiController]
public class ClientAuthController : ControllerBase
{
    private readonly IClientAuthService _authService;

    public ClientAuthController(IClientAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<ClientLoginResponse> Login(ClientLoginRequest request, CancellationToken cancellationToken)
    {
        return await _authService.LoginAsync(request, cancellationToken);
    }
}
