using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Core.Auth.Host.Models;
using Webskt.Core.Auth.Host.Services;

namespace Webskt.Core.Auth.Host.Controllers;

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
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        await _authService.RegisterUserAsync(request.Username, request.Email, request.Password);
        return Ok(new { message = "User registered successfully" });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        try
        {
            var response = await _authService.LoginAsync(request.Email, request.Password, ipAddress);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] string refreshToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        try
        {
            var response = await _authService.RefreshTokenAsync(refreshToken, ipAddress);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [Authorize]
    [HttpGet("check-permission/{permissionSlug}")]
    public async Task<IActionResult> CheckPermission(string permissionSlug)
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
        {
            return Unauthorized();
        }

        var isAllowed = await _authService.ValidatePermissionAsync(userId, permissionSlug);
        return Ok(new { permission = permissionSlug, allowed = isAllowed });
    }
}
