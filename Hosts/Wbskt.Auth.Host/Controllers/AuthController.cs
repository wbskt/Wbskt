using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
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
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Register requested for Username: '{Username}', Email: '{Email}'", request.Username, request.Email);
        var result = await _authService.RegisterUserAsync(request.Username, request.Email, request.Password, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Authenticates a user and returns access and refresh tokens.
    /// </summary>
    /// <param name="request">The login credentials (email, password).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A response containing the JWT access token and refresh token.</returns>
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Login requested for Email: '{Email}'", request.Email);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.LoginAsync(request.Email, request.Password, ipAddress, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Refreshes an expired access token using a valid refresh token.
    /// </summary>
    /// <param name="refreshToken">The valid refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new set of access and refresh tokens.</returns>
    [HttpPost("refresh-token")]
    public async Task<ActionResult<LoginResponse>> RefreshToken([FromBody] string refreshToken, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RefreshToken requested");
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.RefreshTokenAsync(refreshToken, ipAddress, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Checks if the currently authenticated user has a specific permission.
    /// </summary>
    /// <param name="permissionSlug">The unique slug of the permission to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A response indicating if the permission is granted.</returns>
    [Authorize]
    [HttpGet("check-permission/{permissionSlug}")]
    public async Task<ActionResult<PermissionCheckResponse>> CheckPermission(string permissionSlug, CancellationToken cancellationToken)
    {
        _logger.LogDebug("API: CheckPermission requested for slug: '{PermissionSlug}'", permissionSlug);
        var userIdResult = GetCurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapResult(Result.Failure(userIdResult.Error));
        }

        var result = await _authService.VerifyPermissionAsync(userIdResult.Value, permissionSlug, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result.Failure(result.Error));
        }

        return Ok(new PermissionCheckResponse(permissionSlug, result.Value));
    }

    private Result<int> GetCurrentUserId()
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
        {
            return Result<int>.Failure(Error.Unauthorized("AUTH_UNAUTHORIZED", "Unauthorized access."));
        }
        return Result<int>.Success(userId);
    }

    private ActionResult MapResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return MapError(result.Error);
    }

    private ActionResult<T> MapResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapError(result.Error);
    }

    private ActionResult MapError(Error error)
    {
        _logger.LogWarning("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);
        return error.Type switch
        {
            ErrorType.Validation => BadRequest(error),
            ErrorType.NotFound => NotFound(error),
            ErrorType.Conflict => Conflict(error),
            ErrorType.Unauthorized => Unauthorized(error),
            _ => BadRequest(error)
        };
    }
}