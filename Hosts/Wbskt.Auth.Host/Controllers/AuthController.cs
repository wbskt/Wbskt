using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;

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
    /// Registers a new user in the system and mails them a confirmation link. The account cannot
    /// sign in until that link is followed.
    /// </summary>
    /// <remarks>
    /// Answers 204 whether or not the address already has an account. If it does, no account is
    /// created and the existing owner is mailed instead — so this endpoint cannot be used to find
    /// out who is registered. A username that is already taken is still reported as a 409: it
    /// discloses nothing about any address, and a caller retrying a name that can never be accepted
    /// needs to be told.
    /// </remarks>
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
    [EnableRateLimiting(RateLimitPolicies.TokenRefresh)]
    [HttpPost("refresh-token")]
    public async Task<ActionResult<LoginResponse>> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RefreshToken requested");
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Revokes the supplied refresh token, ending that session and the access token issued with it.
    /// Succeeds regardless of whether the token was live, so it cannot be used to probe which tokens
    /// exist.
    /// </summary>
    /// <param name="request">The refresh token to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.TokenRefresh)]
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

    /// <summary>
    /// Changes the signed-in user's password. Requires the current password, ends every session the
    /// account has, and returns a fresh token pair for this caller.
    /// </summary>
    /// <remarks>
    /// A wrong current password answers 400 <c>AUTH_CURRENT_PASSWORD_INVALID</c> rather than 401, so a
    /// client does not read it as an expired session, and counts towards the account lockout like a
    /// failed sign-in. A locked account answers 403 <c>AUTH_ACCOUNT_LOCKED</c>.
    /// </remarks>
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("change-password")]
    public async Task<ActionResult<LoginResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: ChangePassword requested");

        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var result = await _authService.ChangePasswordAsync(userIdResult.Value, request.CurrentPassword, request.NewPassword, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Lists the signed-in user's live sessions (one per signed-in device or browser), newest first.
    /// </summary>
    [Authorize]
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyCollection<SessionResponse>>> GetSessions(CancellationToken cancellationToken)
    {
        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var result = await _authService.GetSessionsAsync(userIdResult.Value, CurrentSessionId(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Ends one of the signed-in user's sessions: its refresh token stops working, and so does the
    /// access token that device holds, on every host. The id is a session's, from the list above,
    /// and stays the same for the life of the session.
    /// </summary>
    [Authorize]
    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RevokeSession requested for session {SessionId}", id);

        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var result = await _authService.RevokeSessionAsync(userIdResult.Value, id, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    private string CallerIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // The default inbound claim map renames sid, so look under both names. Absent on tokens issued
    // before sessions had ids.
    private Guid? CurrentSessionId() =>
        Guid.TryParse(User.FindFirst(JwtServiceCollectionExtensions.JwtSessionClaim)?.Value ?? User.FindFirst(ClaimTypes.Sid)?.Value, out var id)
            ? id
            : null;

    /// <summary>
    /// Requests a password-reset link.
    /// </summary>
    /// <remarks>
    /// Always answers 204, whether or not the address has an account. Telling a caller that an
    /// address is unknown turns this endpoint into a way to enumerate the platform's users.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        // The address is the subject of the request rather than a lookup key, and is logged as such.
        _logger.LogInformation("API: ForgotPassword requested");
        var result = await _authService.ForgotPasswordAsync(request.Email, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Sets a new password using a reset link, and signs the account out everywhere.
    /// </summary>
    /// <remarks>
    /// Every refresh token the account holds is revoked as part of the same transaction that writes
    /// the password. Recovering an account is usually a response to losing control of it, and a
    /// session the attacker already holds must not outlive the reset.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        // The token is a bearer credential and is deliberately absent from this log line.
        _logger.LogInformation("API: ResetPassword requested");
        var result = await _authService.ResetPasswordAsync(request.Token, request.NewPassword, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Confirms an email address using the link sent at registration. The account can sign in once
    /// this succeeds.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.EmailVerification)]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: VerifyEmail requested");
        var result = await _authService.VerifyEmailAsync(request.Token, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Sends a fresh confirmation link.
    /// </summary>
    /// <remarks>
    /// Anonymous, not authenticated: an account that has not confirmed its address cannot sign in,
    /// so it cannot hold a token with which to ask. Answers 204 regardless, like
    /// <see cref="ForgotPassword"/> and for the same reason.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.EmailVerification)]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: ResendVerification requested");
        var result = await _authService.ResendVerificationAsync(request.Email, cancellationToken);
        return MapResult(result);
    }
}
