using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Management.Api.Contracts;
using Wbskt.Management.Api.Services;

namespace Wbskt.Management.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UsersController(
    IUsersReader usersReader,
    IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> UserLogin(UserLoginRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var response = await authService.Login(request, ipAddress, cancellationToken);
        return Ok(response);
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var response = await authService.RotateRefreshToken(request.RefreshToken, ipAddress, cancellationToken);
        return Ok(response);
    }

    [HttpPost("register")]
    public async Task<IActionResult> UserRegistration(UserRegistrationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            request.UserName = request.EmailId;
        }

        var userId = await usersReader.FindByEmailIdAsync(request.EmailId, cancellationToken);
        if (userId > 0)
        {
            return Conflict($"user {request.EmailId} already exists");
        }

        await authService.RegisterUser(request, cancellationToken);
        return Ok("User created. Please login");
    }
}
