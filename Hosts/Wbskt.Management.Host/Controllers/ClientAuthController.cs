using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Controllers;

[Route("api/client-auth")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
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
