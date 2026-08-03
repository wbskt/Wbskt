using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class AuthService : IAuthService
{
    private const string DefaultWorkspaceName = "Default Workspace";

    /// <summary>
    /// One answer for every way an invitation can fail. Registration is anonymous, so distinguishing
    /// "no such token" from "wrong address" would turn it into an oracle for probing invitations.
    /// </summary>
    private static readonly Error InvalidInvitation =
        Error.Validation("INVITATION_INVALID", "This invitation is not valid. It may have expired, been revoked, already been used, or been sent to a different email address.");

    private readonly IAuthProvider _provider;
    private readonly IJwtService _jwtService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthMetrics _metrics;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(
        IAuthProvider provider, 
        IJwtService jwtService, 
        IEventBus eventBus,
        ILogger<AuthService> logger,
        AuthMetrics metrics)
    {
        _provider = provider;
        _jwtService = jwtService;
        _eventBus = eventBus;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<LoginResponse>> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting login for email: {Email} from IP: {IpAddress}", email, ipAddress);

        try 
        {
            User user;
            try
            {
                user = await _provider.GetByEmailAsync(email, cancellationToken);
                _logger.LogDebug("User record resolved for email: {Email}", email);
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Login failed: User with email {Email} not found. IP: {IpAddress}. Error: {Message}", email, ipAddress, ex.Message);
                _logger.LogTrace(ex, "Login user lookup failed stack trace for {Email}", email);
                _metrics.RecordLogin("invalid_credentials");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(-1, Guid.Empty, ipAddress, "Invalid credentials"), cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid credentials."));
            }

            var stopwatch = Stopwatch.StartNew();
            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            stopwatch.Stop();
            _metrics.RecordPasswordHash(stopwatch.Elapsed.TotalMilliseconds);

            if (verificationResult == PasswordVerificationResult.Failed)
            {
                _logger.LogWarning("Login failed: Invalid password for email {Email}. IP: {IpAddress}", email, ipAddress);
                _metrics.RecordLogin("invalid_credentials");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "Invalid password"), cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid credentials."));
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed: Account is inactive for user {Username} ({Email}). IP: {IpAddress}", user.Username, email, ipAddress);
                _metrics.RecordLogin("user_inactive");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "User inactive"), cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_INACTIVE", "User is inactive."));
            }

            _logger.LogDebug("Credentials verified successfully for user: {Username} ({Email}). Generating tokens...", user.Username, email);
            var accessToken = GenerateAccessToken(user);
            var refreshToken = GenerateRefreshToken(user.Id);

            await _provider.InsertRefreshTokenAsync(refreshToken, ipAddress, cancellationToken);
            _logger.LogDebug("Refresh token inserted for user ID: {UserId}", user.Id);

            await _eventBus.PublishAsync(new UserLoginSuccessEvent(user.Id, user.RefId, ipAddress), cancellationToken);
            _logger.LogInformation("User {Username} logged in successfully. IP: {IpAddress}", user.Username, ipAddress);

            _metrics.RecordLogin("success");
            return Result<LoginResponse>.Success(new LoginResponse(accessToken, refreshToken.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error during login for email: {Email}. IP: {IpAddress}. Error: {Message}", email, ipAddress, ex.Message);
            _logger.LogTrace(ex, "Login exception stack trace for {Email}", email);
            _metrics.RecordLogin("error");
            await _eventBus.PublishAsync(new SecurityAlertEvent(
                "LoginFailure", 
                $"Unexpected error during login for {email}: {ex.Message}", 
                ipAddress,
                $"Email: {email}"), cancellationToken);
            
            return Result<LoginResponse>.Failure(Error.Failure("AUTH_LOGIN_ERROR", ex.Message));
        }
    }

    public async Task<Result<LoginResponse>> RefreshTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting token refresh from IP: {IpAddress}", ipAddress);

        try
        {
            RefreshToken existingToken;
            try
            {
                existingToken = await _provider.GetRefreshTokenAsync(token, cancellationToken);
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Token refresh failed: Provided token is invalid. IP: {IpAddress}. Error: {Message}", ipAddress, ex.Message);
                _logger.LogTrace(ex, "Token refresh lookup failed stack trace");
                _metrics.RecordRefresh("invalid_token");
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_TOKEN", "Invalid refresh token."));
            }

            // A token we already retired is being presented again. The legitimate client moved on to
            // its replacement, so whoever sent this either kept a copy or intercepted one. Treat the
            // whole session family as compromised rather than just refusing this one request.
            if (existingToken.Revoked is not null)
            {
                _logger.LogWarning("Token refresh failed: Replay of a revoked token for user ID {UserId}. Revoking all sessions. IP: {IpAddress}", existingToken.UserId, ipAddress);
                await _provider.RevokeAllRefreshTokensForUserAsync(existingToken.UserId, ipAddress, cancellationToken);
                _metrics.RecordRefresh("token_replayed");

                await _eventBus.PublishAsync(new SecurityAlertEvent(
                    "RefreshTokenReplay",
                    $"A revoked refresh token was replayed for user ID {existingToken.UserId}; all sessions revoked.",
                    ipAddress,
                    $"UserId: {existingToken.UserId}"), cancellationToken);

                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_TOKEN_INACTIVE", "Token is no longer active."));
            }

            if (!existingToken.IsActive)
            {
                _logger.LogWarning("Token refresh failed: Provided token for user ID {UserId} has expired. IP: {IpAddress}", existingToken.UserId, ipAddress);
                _metrics.RecordRefresh("token_inactive");
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_TOKEN_INACTIVE", "Token is no longer active."));
            }

            User user;
            try
            {
                user = await _provider.GetByIdAsync(existingToken.UserId, cancellationToken);
                _logger.LogDebug("User record resolved for ID: {UserId}", existingToken.UserId);
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Token refresh failed: User with ID {UserId} not found. IP: {IpAddress}. Error: {Message}", existingToken.UserId, ipAddress, ex.Message);
                _logger.LogTrace(ex, "Token refresh user lookup failed stack trace for User ID {UserId}", existingToken.UserId);
                _metrics.RecordRefresh("user_not_found");
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_NOT_FOUND", "User not found."));
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Token refresh failed: Account is inactive for user {Username}. IP: {IpAddress}", user.Username, ipAddress);
                _metrics.RecordRefresh("user_inactive");
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_INACTIVE", "User is inactive."));
            }

            _logger.LogDebug("Rotating refresh token for user: {Username}", user.Username);
            var newRefreshToken = GenerateRefreshToken(user.Id);

            // Retire the presented token *before* minting its replacement, and let the revoke decide
            // who won. It is a compare-and-swap - it only touches a row whose Revoked is still null -
            // and reports how many rows it retired, so exactly one of two concurrent exchanges can
            // come away with a count of 1. Minting first and revoking afterwards let both callers
            // through the earlier IsActive check and both walk away with a live token, which turns
            // one refresh token into two independent session families. That is precisely what the
            // replay detection above exists to prevent: a thief racing the legitimate client would
            // otherwise hold a family that survives the victim's next rotation.
            var revoked = await _provider.RevokeRefreshTokenAsync(token, ipAddress, newRefreshToken.Token, cancellationToken);
            if (revoked == 0)
            {
                // Another exchange retired this token in the moment between our read and our write.
                // Losing that race is not evidence of theft - the token was used exactly once, just
                // not by us - so the family is left alone and only this request is refused.
                _logger.LogWarning("Token refresh lost a concurrent exchange for user ID {UserId}. IP: {IpAddress}", user.Id, ipAddress);
                _metrics.RecordRefresh("token_raced");
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_TOKEN_INACTIVE", "Token is no longer active."));
            }

            var newAccessToken = GenerateAccessToken(user);
            await _provider.InsertRefreshTokenAsync(newRefreshToken, ipAddress, cancellationToken);

            await _eventBus.PublishAsync(new TokenRotatedEvent(user.Id, user.RefId, ipAddress), cancellationToken);

            _logger.LogInformation("Token refreshed successfully for user: {Username}. IP: {IpAddress}", user.Username, ipAddress);
            _metrics.RecordRefresh("success");
            return Result<LoginResponse>.Success(new LoginResponse(newAccessToken, newRefreshToken.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error during token refresh from IP: {IpAddress}. Error: {Message}", ipAddress, ex.Message);
            _logger.LogTrace(ex, "Token refresh exception stack trace");
            _metrics.RecordRefresh("error");
            return Result<LoginResponse>.Failure(Error.Failure("AUTH_REFRESH_ERROR", ex.Message));
        }
    }

    public async Task<Result> LogoutAsync(string token, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting logout from IP: {IpAddress}", ipAddress);

        try
        {
            // Deliberately not reporting whether the token existed: logout is unauthenticated by
            // necessity, and a distinguishable response would turn it into a token oracle.
            var revoked = await _provider.RevokeRefreshTokenAsync(token, ipAddress, null, cancellationToken);
            _logger.LogInformation("Logout revoked {RevokedCount} refresh token(s). IP: {IpAddress}", revoked, ipAddress);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error during logout from IP: {IpAddress}. Error: {Message}", ipAddress, ex.Message);
            _logger.LogTrace(ex, "Logout exception stack trace");
            return Result.Failure(Error.Failure("AUTH_LOGOUT_ERROR", ex.Message));
        }
    }

    public async Task<Result> LogoutAllAsync(int userId, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to revoke all sessions for user ID: {UserId} from IP: {IpAddress}", userId, ipAddress);

        try
        {
            var revoked = await _provider.RevokeAllRefreshTokensForUserAsync(userId, ipAddress, cancellationToken);
            _logger.LogInformation("Revoked {RevokedCount} refresh token(s) for user ID: {UserId}", revoked, userId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error revoking sessions for user ID: {UserId}. Error: {Message}", userId, ex.Message);
            _logger.LogTrace(ex, "LogoutAll exception stack trace for user {UserId}", userId);
            return Result.Failure(Error.Failure("AUTH_LOGOUT_ERROR", ex.Message));
        }
    }

    public async Task<Result> RegisterUserAsync(string username, string email, string password, string? invitationToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to register user: {Username} with email: {Email}", username, email);

        // Checked before the account is created, not after. Redeeming an invitation is the last step
        // of registration, so a token that turns out to be unusable would otherwise leave behind an
        // account the user cannot register again — their email is taken — and did not want on its own.
        if (invitationToken is not null)
        {
            var invitation = await ValidateInvitationAsync(invitationToken, email, cancellationToken);
            if (invitation.IsFailure)
            {
                _metrics.RecordRegistration("invalid_invitation");
                return invitation;
            }
        }

        try
        {
            var user = new User
            {
                Username = username,
                Email = email
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            var userId = await _provider.InsertUserAsync(user, cancellationToken);
            _logger.LogDebug("User record inserted with database ID: {UserId}", userId);

            // Everyone gets their own tenant, invited or not. It is where a single user's workspaces
            // live, and it means a member who later leaves the tenant that invited them still has
            // somewhere to be — an account belonging to no tenant can do nothing at all and no
            // endpoint can repair it.
            var tenantRefId = await _provider.CreateTenantAsync($"{username}'s Tenant", null, userId, DefaultWorkspaceName, cancellationToken);
            _logger.LogInformation("Created tenant {TenantRefId} for new user ID {UserId}", tenantRefId, userId);

            if (invitationToken is not null)
            {
                // Re-validated authoritatively inside the procedure. Between the check above and here
                // the invitation could have been revoked or redeemed, in which case this throws and
                // registration fails — the account and its own tenant survive, so the user can log in
                // and ask for a fresh invitation rather than being stranded.
                var tenantId = await _provider.AcceptInvitationAsync(InvitationTokens.Hash(invitationToken), userId, cancellationToken);
                _logger.LogInformation("New user ID {UserId} joined tenant ID {TenantId} by invitation", userId, tenantId);
            }

            user = await _provider.GetByIdAsync(userId, cancellationToken);

            await _eventBus.PublishAsync(new UserRegisteredEvent(userId, user.RefId, username, email), cancellationToken);
            _logger.LogInformation("User {Username} registered successfully. RefId: {RefId}", username, user.RefId);
            
            _metrics.RecordRegistration("success");
            return Result.Success();
        }
        catch (Exception ex)
        {
            // The invitation was revoked or redeemed between the pre-check and the redemption. The
            // account and its own tenant exist and are usable, so this is the caller's problem to
            // retry with a fresh invitation, not a server fault — 400, matching the pre-check.
            if (ex is SqlException { Number: 50011 })
            {
                _logger.LogWarning("Registration for {Username} completed but the invitation was no longer redeemable", username);
                _metrics.RecordRegistration("invalid_invitation");
                return Result.Failure(InvalidInvitation);
            }

            if (ex is SqlException { Number: 2601 or 2627 })
            {
                _logger.LogWarning("User registration failed: Conflict on Username={Username} or Email={Email}. Error: {Message}", username, email, ex.Message);
                _logger.LogTrace(ex, "User registration conflict stack trace for {Username}", username);
                _metrics.RecordRegistration("conflict");
                return Result.Failure(Error.Conflict("AUTH_USER_CONFLICT", "Username or email is already registered."));
            }
            _logger.LogError("Failed to register user: {Username} ({Email}). Error: {Message}", username, email, ex.Message);
            _logger.LogTrace(ex, "User registration failure stack trace for {Username}", username);
            _metrics.RecordRegistration("error");
            return Result.Failure(Error.Failure("AUTH_REGISTRATION_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Confirms a token names a live invitation addressed to <paramref name="email"/>, before any
    /// account exists. Every rejection is reported identically, so an unauthenticated caller cannot
    /// use registration to discover whether a token is real or which address it was issued to.
    /// </summary>
    private async Task<Result> ValidateInvitationAsync(string invitationToken, string email, CancellationToken cancellationToken)
    {
        InvitationLookup invitation;
        try
        {
            invitation = await _provider.GetInvitationByTokenHashAsync(InvitationTokens.Hash(invitationToken), cancellationToken);
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Registration rejected: no invitation matches the presented token");
            return Result.Failure(InvalidInvitation);
        }

        // Ordinal-ignore-case, matching how SQL Server compares under the database's default
        // collation — so the check here agrees with the one TenantInvitation_Accept applies.
        if (!invitation.IsLive || !string.Equals(invitation.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Registration rejected: invitation {RefId} is not live, or was issued to a different address", invitation.RefId);
            return Result.Failure(InvalidInvitation);
        }

        return Result.Success();
    }

    private string GenerateAccessToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("type", "user")
        };

        return _jwtService.GenerateToken(claims, TimeSpan.FromMinutes(60));
    }

    private RefreshToken GenerateRefreshToken(int userId)
    {
        using var rng = RandomNumberGenerator.Create();
        var randomBytes = new byte[64];
        rng.GetBytes(randomBytes);

        return new RefreshToken
        {
            UserId = userId,
            Token = Convert.ToBase64String(randomBytes),
            Expires = DateTime.UtcNow.AddDays(7),
        };
    }
}