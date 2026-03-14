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

    /// <summary>
    /// Registers a new user in the system.
    /// </summary>
    /// <param name="request">The registration details (username, email, password).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("register")]
    public async Task Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        await _authService.RegisterUserAsync(request.Username, request.Email, request.Password, cancellationToken);
    }

    /// <summary>
    /// Authenticates a user and returns access and refresh tokens.
    /// </summary>
    /// <param name="request">The login credentials (email, password).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A response containing the JWT access token and refresh token.</returns>
    [HttpPost("login")]
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var response = await _authService.LoginAsync(request.Email, request.Password, ipAddress, cancellationToken);

        return response;
    }

    /// <summary>
    /// Refreshes an expired access token using a valid refresh token.
    /// </summary>
    /// <param name="refreshToken">The valid refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new set of access and refresh tokens.</returns>
    [HttpPost("refresh-token")]
    public async Task<LoginResponse> RefreshToken([FromBody] string refreshToken, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var response = await _authService.RefreshTokenAsync(refreshToken, ipAddress, cancellationToken);

        return response;
    }

    /// <summary>
    /// Checks if the currently authenticated user has a specific permission.
    /// </summary>
    /// <param name="permissionSlug">The unique slug of the permission to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A response indicating if the permission is granted.</returns>
    /// <exception cref="SecurityException">Thrown if the user ID is not found in the token.</exception>
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