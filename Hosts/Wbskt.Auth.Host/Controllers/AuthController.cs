using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Registers a new user in the system.
    /// </summary>
    /// <param name="request">The registration details (username, email, password).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Register requested for Username: '{Username}', Email: '{Email}'", request.Username, request.Email);
        var result = await _authService.RegisterUserAsync(request.Username, request.Email, request.Password, request.InvitationToken, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Authenticates a user and returns access and refresh tokens.
    /// </summary>
    /// <param name="request">The login credentials (email, password).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A response containing the JWT access token and refresh token.</returns>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Login requested for Email: '{Email}'", request.Email);
        var result = await _authService.LoginAsync(request.Email, request.Password, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Exchanges a valid refresh token for a new access token and a new refresh token.
    /// The presented token is revoked as part of the exchange, so it cannot be used again.
    /// </summary>
    /// <param name="request">The valid refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new set of access and refresh tokens.</returns>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("refresh-token")]
    public async Task<ActionResult<LoginResponse>> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RefreshToken requested");
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Revokes the supplied refresh token, ending that session. Succeeds regardless of whether the
    /// token was live, so it cannot be used to probe which tokens exist.
    /// </summary>
    /// <param name="request">The refresh token to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Logout requested");
        var result = await _authService.LogoutAsync(request.RefreshToken, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Revokes every refresh token belonging to the current user, signing them out everywhere.
    /// Access tokens already issued remain valid until they expire.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: LogoutAll requested");

        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var result = await _authService.LogoutAllAsync(userIdResult.Value, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    private string CallerIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
