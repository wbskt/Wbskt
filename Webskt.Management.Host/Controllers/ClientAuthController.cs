using Microsoft.AspNetCore.Mvc;
using Webskt.Management.Host.Models;
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
    public async Task<ClientLoginResponse> Login(ClientLoginRequest request)
    {
        return await _authService.LoginAsync(request);
    }
}
