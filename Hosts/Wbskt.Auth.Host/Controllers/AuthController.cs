using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Common.Abstraction.Models.Auth;
using Wbskt.Foundation.Abstraction.Exceptions;

namespace Wbskt.Auth.Host.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        await _authService.RegisterUserAsync(request.Username, request.Email, request.Password, cancellationToken);
    }

    [HttpPost("login")]
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var response = await _authService.LoginAsync(request.Email, request.Password, ipAddress, cancellationToken);

        return response;
    }

    [HttpPost("refresh-token")]
    public async Task<LoginResponse> RefreshToken([FromBody] string refreshToken, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var response = await _authService.RefreshTokenAsync(refreshToken, ipAddress, cancellationToken);

        return response;
    }

    [Authorize]
    [HttpGet("check-permission/{permissionSlug}")]
    public async Task<PermissionCheckResponse> CheckPermission(string permissionSlug, CancellationToken cancellationToken)
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
        {
            throw new SecurityException("Unauthorized access.");
        }

        var isAllowed = await _authService.VerifyPermissionAsync(userId, permissionSlug, cancellationToken);

        return new PermissionCheckResponse(permissionSlug, isAllowed);
    }
}