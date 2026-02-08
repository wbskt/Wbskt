using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Auth.Host.Models;
using Webskt.Auth.Host.Services;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Models.Auth;

namespace Webskt.Auth.Host.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task Register(RegisterRequest request)
    {
        await _authService.RegisterUserAsync(request.Username, request.Email, request.Password);
    }

    [HttpPost("login")]
    public async Task<LoginResponse> Login(LoginRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var response = await _authService.LoginAsync(request.Email, request.Password, ipAddress);

        return response;
    }

    [HttpPost("refresh-token")]
    public async Task<LoginResponse> RefreshToken([FromBody] string refreshToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var response = await _authService.RefreshTokenAsync(refreshToken, ipAddress);

        return response;
    }

    [Authorize]
    [HttpGet("check-permission/{permissionSlug}")]
    public async Task<PermissionCheckResponse> CheckPermission(string permissionSlug)
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
        {
            throw new SecurityException("Unauthorized access.");
        }

        var isAllowed = await _authService.VerifyPermissionAsync(userId, permissionSlug);

        return new PermissionCheckResponse(permissionSlug, isAllowed);
    }
}