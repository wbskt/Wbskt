using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Email;
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

    /// <summary>
    /// One answer for an unknown, spent or expired recovery token, matching how the procedure that
    /// consumes one reports all three. Anything more specific says whether a token ever existed.
    /// </summary>
    private static readonly Error InvalidResetToken =
        Error.Validation("RESET_TOKEN_INVALID", "This password reset link is not valid. It may have expired or already been used. Request a new one.");

    private static readonly Error InvalidVerificationToken =
        Error.Validation("VERIFICATION_TOKEN_INVALID", "This confirmation link is not valid. It may have expired or already been used. Request a new one.");

    /// <summary>
    /// Short, because a reset link in an unattended inbox is a live credential for the account. An
    /// hour is long enough to survive a user reading mail on another device.
    /// </summary>
    private static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// Longer than a reset link, deliberately. This one is not a credential for an existing account -
    /// it only ever turns an unusable account into a usable one - and sign-up commonly happens
    /// minutes before someone stops looking at their inbox for the day. An hour here would mean
    /// anyone who registers in the evening finds a dead link in the morning.
    /// </summary>
    private static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Wrong passwords an account takes before it is locked. High enough that a person mistyping is
    /// never caught by it, low enough that a guesser spread across many addresses - which the per-IP
    /// rate limiter cannot see - gets nowhere.
    /// </summary>
    internal const int MaxFailedLogins = 10;

    /// <summary>
    /// Long enough to make guessing pointless, short enough that a lock someone else caused on purpose
    /// is a nuisance rather than an outage. A password reset lifts it at once.
    /// </summary>
    internal static readonly TimeSpan LoginLockout = TimeSpan.FromMinutes(15);

    private readonly IAuthProvider _provider;
    private readonly IJwtService _jwtService;
    private readonly IEventBus _eventBus;
    private readonly IAuthMailer _mailer;
    private readonly MailCooldown _mailCooldown;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthMetrics _metrics;
    private readonly IAccessTokenRevocation _accessTokens;
    private readonly TimeSpan _accessTokenLifetime;
    private readonly TimeSpan _refreshTokenLifetime;
    private readonly TimeSpan _sessionLifetime;
    private readonly bool _requireVerifiedEmail;
    private readonly IAuditWorkspaces _audit;
    private readonly PasswordHasher<User> _passwordHasher = new();

    /// <summary>
    /// A hash of a password nobody knows, verified against when there is no real one to check, so a
    /// login for an address with no account (or a locked one) costs the same hashing work as one with
    /// a wrong password. Without it, the time a login takes says whether the address is registered -
    /// the one thing register, forgot-password and resend-verification are built not to reveal.
    /// </summary>
    private static readonly Lazy<string> DummyPasswordHash =
        new(() => new PasswordHasher<User>().HashPassword(new User(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public AuthService(
        IAuthProvider provider, 
        IJwtService jwtService, 
        IEventBus eventBus,
        IAuthMailer mailer,
        IOptions<AuthEmailOptions> emailOptions,
        ILogger<AuthService> logger,
        AuthMetrics metrics,
        IAccessTokenRevocation accessTokens,
        IOptions<AccessTokenOptions> accessTokenOptions,
        MailCooldown mailCooldown,
        IAuditWorkspaces? audit = null)
    {
        _audit = audit ?? AuditWorkspaces.None;
        _accessTokens = accessTokens;
        _accessTokenLifetime = accessTokenOptions.Value.AccessTokenLifetime;
        _refreshTokenLifetime = accessTokenOptions.Value.RefreshTokenLifetime;
        _sessionLifetime = accessTokenOptions.Value.SessionLifetime;
        _provider = provider;
        _jwtService = jwtService;
        _eventBus = eventBus;
        _mailer = mailer;
        _mailCooldown = mailCooldown;
        _requireVerifiedEmail = emailOptions.Value.RequireVerifiedEmailForSignIn;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<LoginResponse>> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Attempting login from IP: {IpAddress}", ipAddress);

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
                SpendPasswordCheck(password);
                _logger.LogWarning("Login failed: User with email {Email} not found. IP: {IpAddress}. Error: {Message}", email, ipAddress, ex.Message);
                _logger.LogTrace(ex, "Login user lookup failed stack trace for {Email}", email);
                _metrics.RecordLogin("invalid_credentials");
                // The same lookup the known-account answers make for their audit entry, so an unknown
                // address is not told apart by answering sooner. No account, so no workspace.
                await _audit.OfUserAsync(0, cancellationToken);
                await _eventBus.PublishAsync(new UserLoginFailedEvent(-1, Guid.Empty, ipAddress, "Invalid credentials"), cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid credentials."));
            }

            // Before the password check, so a locked account cannot be used to test passwords. The
            // answer is the ordinary one: a distinct "locked" reply would confirm that the address has
            // an account to anyone willing to make ten bad guesses. The owner gets in by resetting.
            if (user.LockedUntil > DateTime.UtcNow)
            {
                // Against the dummy hash, not the account's: the work is spent, the password is not tested.
                SpendPasswordCheck(password);
                _logger.LogWarning("Login refused: account {UserId} is locked until {LockedUntil}. IP: {IpAddress}", user.Id, user.LockedUntil, ipAddress);
                _metrics.RecordLogin("locked_out");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "Account locked") { WorkspaceIds = await _audit.OfUserAsync(user.Id, cancellationToken) }, cancellationToken);
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
                await RecordFailedPasswordAsync(user, ipAddress, cancellationToken);
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "Invalid password") { WorkspaceIds = await _audit.OfUserAsync(user.Id, cancellationToken) }, cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid credentials."));
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed: Account is inactive for user {Username} ({Email}). IP: {IpAddress}", user.Username, email, ipAddress);
                _metrics.RecordLogin("user_inactive");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "User inactive") { WorkspaceIds = await _audit.OfUserAsync(user.Id, cancellationToken) }, cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_INACTIVE", "User is inactive."));
            }

            // After the password check, not before: answering "verify your address" to someone who did
            // not supply the right password would confirm the account exists to anyone who asks.
            if (!user.IsEmailVerified && !_requireVerifiedEmail)
            {
                _logger.LogWarning(
                    "Signing in user {Username} with an unconfirmed address because Auth:Email:RequireVerifiedEmailForSignIn is false. " +
                    "This must not be the case outside local development.", user.Username);
            }
            else if (!user.IsEmailVerified)
            {
                _logger.LogWarning("Login refused: address not yet verified for user {Username} ({Email}). IP: {IpAddress}", user.Username, email, ipAddress);
                _metrics.RecordLogin("email_unverified");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "Email not verified") { WorkspaceIds = await _audit.OfUserAsync(user.Id, cancellationToken) }, cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized(
                    "AUTH_EMAIL_UNVERIFIED",
                    "Confirm your email address before signing in. Check your inbox, or ask for a new confirmation link."));
            }

            await _provider.RecordLoginSuccessAsync(user.Id, cancellationToken);

            if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                await UpgradePasswordHashAsync(user, password, cancellationToken);
            }

            _logger.LogDebug("Credentials verified successfully for user: {Username} ({Email}). Generating tokens...", user.Username, email);
            var refreshToken = GenerateRefreshToken(user.Id);
            var accessToken = GenerateAccessToken(user, refreshToken.SessionId);

            await _provider.InsertRefreshTokenAsync(refreshToken, ipAddress, cancellationToken);
            _logger.LogDebug("Refresh token inserted for user ID: {UserId}", user.Id);

            await _eventBus.PublishAsync(new UserLoginSuccessEvent(user.Id, user.RefId, ipAddress) { WorkspaceIds = await _audit.OfUserAsync(user.Id, cancellationToken) }, cancellationToken);
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
        _logger.LogDebug("Attempting token refresh from IP: {IpAddress}", ipAddress);

        try
        {
            // One call, one transaction: the presented token is retired and its replacement stored
            // together or not at all. When these were separate calls, a failure after the revoke left
            // the client holding a dead token, and its next attempt read as a replay that ended every
            // session the user had. The procedure also tells a replay (already retired when read)
            // from a lost race (retired by a concurrent exchange in between); see RefreshToken_Rotate.
            // The user id and session are not needed here: the procedure stores the replacement under
            // whoever owns the presented token, in the same session, and refuses to extend a session
            // past its absolute lifetime.
            var newRefreshToken = GenerateRefreshToken(0);
            var rotation = await _provider.RotateRefreshTokenAsync(token, newRefreshToken, _sessionLifetime, ipAddress, cancellationToken);

            switch (rotation.Outcome)
            {
                case RefreshRotationOutcome.Unknown:
                    _logger.LogWarning("Token refresh failed: Provided token is invalid. IP: {IpAddress}", ipAddress);
                    _metrics.RecordRefresh("invalid_token");
                    return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_TOKEN", "Invalid refresh token."));

                case RefreshRotationOutcome.Replayed:
                {
                    // A token we already retired is being presented again. The legitimate client moved
                    // on to its replacement, so whoever sent this either kept a copy or intercepted one.
                    // The procedure has already revoked every refresh token the user holds; this ends
                    // their access tokens too.
                    var userId = rotation.UserId!.Value;
                    _logger.LogWarning("Token refresh failed: Replay of a revoked token for user ID {UserId}. All sessions revoked. IP: {IpAddress}", userId, ipAddress);
                    await _accessTokens.RevokeUserAsync(userId, cancellationToken);
                    _metrics.RecordRefresh("token_replayed");

                    await _eventBus.PublishAsync(new SecurityAlertEvent(
                        "RefreshTokenReplay",
                        $"A revoked refresh token was replayed for user ID {userId}; all sessions revoked.",
                        ipAddress,
                        $"UserId: {userId}"), cancellationToken);

                    return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_TOKEN_INACTIVE", "Token is no longer active."));
                }

                case RefreshRotationOutcome.Expired:
                    _logger.LogWarning("Token refresh failed: Provided token for user ID {UserId} has expired. IP: {IpAddress}", rotation.UserId, ipAddress);
                    _metrics.RecordRefresh("token_inactive");
                    return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_TOKEN_INACTIVE", "Token is no longer active."));

                case RefreshRotationOutcome.SessionExpired:
                    // Used often enough to stay alive, but signed in too long ago. The same answer as
                    // an expired token: the client signs in again, with the password.
                    _logger.LogInformation("Token refresh refused: session for user ID {UserId} reached its absolute lifetime. IP: {IpAddress}", rotation.UserId, ipAddress);
                    _metrics.RecordRefresh("session_expired");
                    return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_TOKEN_INACTIVE", "Token is no longer active."));

                case RefreshRotationOutcome.UserInactive:
                    _logger.LogWarning("Token refresh failed: Account is inactive for user ID {UserId}. IP: {IpAddress}", rotation.UserId, ipAddress);
                    _metrics.RecordRefresh("user_inactive");
                    return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_INACTIVE", "User is inactive."));

                case RefreshRotationOutcome.Raced:
                    // Another exchange retired this token in the moment between the procedure's read
                    // and its write. Losing that race is not evidence of theft - the token was used
                    // exactly once, just not by us - so the family is left alone and only this request
                    // is refused.
                    _logger.LogWarning("Token refresh lost a concurrent exchange for user ID {UserId}. IP: {IpAddress}", rotation.UserId, ipAddress);
                    _metrics.RecordRefresh("token_raced");
                    return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_TOKEN_INACTIVE", "Token is no longer active."));
            }

            var user = rotation.User!;
            var newAccessToken = GenerateAccessToken(user, rotation.SessionId!.Value);

            // Queued, not awaited on the broker (QueuedEventBus): the rotation is committed, and an
            // event that fails to go out must not turn it into an error the client acts on.
            await _eventBus.PublishAsync(new TokenRotatedEvent(user.Id, user.RefId, ipAddress), cancellationToken);

            // Debug: the most frequent call this host serves. AuthMetrics counts the outcomes.
            _logger.LogDebug("Token refreshed for user {UserId}. IP: {IpAddress}", user.Id, ipAddress);
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
        _logger.LogDebug("Attempting logout from IP: {IpAddress}", ipAddress);

        try
        {
            // Deliberately not reporting whether the token existed: logout is unauthenticated by
            // necessity, and a distinguishable response would turn it into a token oracle.
            var sessionId = await _provider.RevokeRefreshTokenAsync(token, ipAddress, null, cancellationToken);
            if (sessionId is not null)
            {
                // The refresh token is gone, so this ends the access token the device still holds.
                await _accessTokens.RevokeSessionAsync(sessionId.Value, cancellationToken);
            }

            _logger.LogInformation("Logout {Outcome}. IP: {IpAddress}", sessionId is null ? "found no live token" : "ended a session", ipAddress);
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
            await _accessTokens.RevokeUserAsync(userId, cancellationToken);
            _logger.LogInformation("Revoked {RevokedCount} refresh token(s) for user ID: {UserId}", revoked, userId);
            await AnnounceAsync(new SessionsRevokedEvent(userId, await RefOfAsync(userId, cancellationToken), "all"), ipAddress, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error revoking sessions for user ID: {UserId}. Error: {Message}", userId, ex.Message);
            _logger.LogTrace(ex, "LogoutAll exception stack trace for user {UserId}", userId);
            return Result.Failure(Error.Failure("AUTH_LOGOUT_ERROR", ex.Message));
        }
    }

    public async Task<Result<LoginResponse>> ChangePasswordAsync(int userId, string currentPassword, string newPassword, string ipAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _provider.GetByIdAsync(userId, cancellationToken);

            // The caller is signed in as this account, so saying it is locked discloses nothing - and
            // checking the current password while locked would reopen the guessing the lock closed.
            if (user.LockedUntil > DateTime.UtcNow)
            {
                _logger.LogWarning("Password change refused: account {UserId} is locked. IP: {IpAddress}", userId, ipAddress);
                return Result<LoginResponse>.Failure(Error.Forbidden("AUTH_ACCOUNT_LOCKED", "Too many wrong passwords. Try again later, or reset your password."));
            }

            if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            {
                // Counted like a failed sign-in: a stolen access token must not become an unthrottled
                // way to guess the password behind it.
                _logger.LogWarning("Password change refused: current password did not match for user {UserId}. IP: {IpAddress}", userId, ipAddress);
                await RecordFailedPasswordAsync(user, ipAddress, cancellationToken);
                // 400, not 401: the session is fine, and a 401 would make a client sign the user out.
                return Result<LoginResponse>.Failure(Error.Validation("AUTH_CURRENT_PASSWORD_INVALID", "The current password is not correct."));
            }

            await _provider.ChangePasswordAsync(userId, _passwordHasher.HashPassword(user, newPassword), ipAddress, cancellationToken);
            await _accessTokens.RevokeUserAsync(userId, cancellationToken);

            // Every session ended with the old password, including this one. The caller gets a new
            // pair so changing a password does not also sign them out.
            var refreshToken = GenerateRefreshToken(userId);
            await _provider.InsertRefreshTokenAsync(refreshToken, ipAddress, cancellationToken);

            _logger.LogInformation("Password changed for user {UserId}; all other sessions revoked. IP: {IpAddress}", userId, ipAddress);
            await AnnounceAsync(new PasswordChangedEvent(userId, user.RefId, "changed"), ipAddress, cancellationToken);
            return Result<LoginResponse>.Success(new LoginResponse(GenerateAccessToken(user, refreshToken.SessionId), refreshToken.Token));
        }
        catch (SecurityException)
        {
            return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_NOT_FOUND", "User not found."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error changing the password for user {UserId}", userId);
            return Result<LoginResponse>.Failure(Error.Failure("AUTH_CHANGE_PASSWORD_ERROR", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyCollection<SessionResponse>>> GetSessionsAsync(int userId, Guid? currentSessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessions = await _provider.GetActiveSessionsAsync(userId, cancellationToken);
            return Result<IReadOnlyCollection<SessionResponse>>.Success(
                sessions.Select(s => s with { IsCurrent = s.Id == currentSessionId }).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing sessions for user {UserId}", userId);
            return Result<IReadOnlyCollection<SessionResponse>>.Failure(Error.Failure("AUTH_SESSIONS_ERROR", ex.Message));
        }
    }

    public async Task<Result> RevokeSessionAsync(int userId, Guid sessionId, string ipAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            var revoked = await _provider.RevokeSessionAsync(sessionId, userId, ipAddress, cancellationToken);
            if (revoked == 0)
            {
                // Someone else's session reads exactly like one that does not exist.
                return Result.Failure(Error.NotFound("AUTH_SESSION_NOT_FOUND", "Session not found."));
            }

            // After the refresh token, so the device cannot mint an access token the revocation misses.
            await _accessTokens.RevokeSessionAsync(sessionId, cancellationToken);

            _logger.LogInformation("User {UserId} ended session {SessionId}. IP: {IpAddress}", userId, sessionId, ipAddress);
            await AnnounceAsync(new SessionsRevokedEvent(userId, await RefOfAsync(userId, cancellationToken), "one"), ipAddress, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error ending session {SessionId} for user {UserId}", sessionId, userId);
            return Result.Failure(Error.Failure("AUTH_SESSIONS_ERROR", ex.Message));
        }
    }

    public async Task<Result> RegisterUserAsync(string username, string email, string password, string? invitationToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Attempting to register user: {Username}", username);

        // Checked before the account is created, not after. Redeeming an invitation is the last step
        // of registration, so a token that turns out to be unusable would otherwise leave behind an
        // account the user cannot register again — their email is taken — and did not want on its own.
        InvitationLookup? invitation = null;
        if (invitationToken is not null)
        {
            var validated = await ValidateInvitationAsync(invitationToken, email, cancellationToken);
            if (validated.IsFailure)
            {
                _metrics.RecordRegistration("invalid_invitation");
                return Result.Failure(validated.Error);
            }

            invitation = validated.Value;
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

            int? joinedTenantId = null;
            if (invitationToken is not null)
            {
                // Re-validated authoritatively inside the procedure. Between the check above and here
                // the invitation could have been revoked or redeemed, in which case this throws and
                // registration fails — the account and its own tenant survive, so the user can log in
                // and ask for a fresh invitation rather than being stranded.
                var tenantId = await _provider.AcceptInvitationAsync(SecurityTokens.Hash(invitationToken), userId, cancellationToken);
                _logger.LogInformation("New user ID {UserId} joined tenant ID {TenantId} by invitation", userId, tenantId);
                joinedTenantId = tenantId;
            }

            user = await _provider.GetByIdAsync(userId, cancellationToken);

            // The account exists and is unusable until this link is followed. Issued as its own step
            // rather than inside a transaction with the rows above, because there is no ambient
            // transaction here to join - the user insert and Tenant_Create are already separate calls,
            // and Tenant_Create is the only one that is atomic internally. If this step or the mail
            // fails, resend-verification is the way back, which is why it is anonymous.
            //
            // Always sent: the address had no account a moment ago, so nothing has been mailed to
            // it. Still counted, so a resend straight after signing up waits out the cooldown.
            await _mailCooldown.TryAcquireAsync(user.Email, MailKind.EmailVerification);
            await IssueEmailVerificationAsync(user, cancellationToken);

            await _eventBus.PublishAsync(new UserRegisteredEvent(userId, user.RefId, username, email), cancellationToken);
            if (joinedTenantId is { } joined && invitation is not null)
            {
                await _eventBus.PublishAsync(
                    new InvitationAcceptedEvent(invitation.RefId, invitation.Email, userId, user.RefId) { WorkspaceIds = await _audit.OfTenantAsync(joined, cancellationToken) },
                    cancellationToken);
            }
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
                return await HandleRegistrationConflictAsync(username, email, ex, cancellationToken);
            }
            _logger.LogError("Failed to register user: {Username} ({Email}). Error: {Message}", username, email, ex.Message);
            _logger.LogTrace(ex, "User registration failure stack trace for {Username}", username);
            _metrics.RecordRegistration("error");
            return Result.Failure(Error.Failure("AUTH_REGISTRATION_ERROR", ex.Message));
        }
    }

    public async Task<Result> ForgotPasswordAsync(string email, string ipAddress, CancellationToken cancellationToken = default)
    {
        // 204 on every path below. Whether an address has an account is exactly what an attacker
        // wants from this endpoint, so the status, the body and the absence of a body are identical
        // for a real address, an unknown one, and a deactivated one.
        //
        // Timing is not equalised. The hit path does one extra insert, roughly a millisecond against
        // a lookup that both paths pay - a signal, but a far smaller one than a different response,
        // and closing it properly means doing fake work rather than less. Recorded rather than fixed.
        User user;
        try
        {
            user = await _provider.GetByEmailAsync(email, cancellationToken);
        }
        catch (SecurityException)
        {
            _logger.LogInformation("Password reset requested for an address with no account. IP: {IpAddress}", ipAddress);
            return Result.Success();
        }

        if (!user.IsActive)
        {
            // A deactivated account must not be recoverable by its former owner - reactivating is an
            // administrator's decision. Answering identically keeps that from being discoverable.
            _logger.LogWarning("Password reset requested for deactivated user {UserId}. IP: {IpAddress}", user.Id, ipAddress);
            return Result.Success();
        }

        // Before the token, not after: a new token supersedes the last one, and superseding it
        // without a mail would leave the owner holding a dead link.
        if (!await _mailCooldown.TryAcquireAsync(user.Email, MailKind.PasswordReset))
        {
            _logger.LogInformation("Password reset for user {UserId} suppressed by the mail cooldown. IP: {IpAddress}", user.Id, ipAddress);
            _metrics.RecordMailSuppressed(nameof(MailKind.PasswordReset));
            return Result.Success();
        }

        try
        {
            var token = SecurityTokens.Generate();
            var expiresAt = DateTime.UtcNow.Add(PasswordResetLifetime);

            await _provider.CreatePasswordResetTokenAsync(user.Id, SecurityTokens.Hash(token), expiresAt, ipAddress, cancellationToken);
            await _mailer.QueuePasswordResetAsync(user.Email, user.Username, token, expiresAt, cancellationToken);

            // The raw token is not logged here or anywhere else.
            _logger.LogInformation("Password reset issued for user {UserId}. IP: {IpAddress}", user.Id, ipAddress);
        }
        catch (Exception ex)
        {
            // Still 204. A failure here is ours, and reporting it would be a way to tell a real
            // address from an unknown one by which requests can be made to fail.
            _logger.LogError(ex, "Failed to issue a password reset for user {UserId}", user.Id);
        }

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(string token, string newPassword, string ipAddress, CancellationToken cancellationToken = default)
    {
        // Hashed before the token is checked, so an invalid token costs the same work as a valid one
        // and cannot be distinguished by how quickly it comes back. PasswordHasher<T> ignores the
        // user argument when hashing, so a bare instance is correct here - there is no account yet.
        var passwordHash = _passwordHasher.HashPassword(new User(), newPassword);

        int userId;
        try
        {
            // One call, one transaction: the new password and the revocation of every existing
            // session land together or not at all. Whoever the victim is resetting away from is
            // often already holding a session, and a gap between the two writes is that session
            // surviving the reset.
            userId = await _provider.ConsumePasswordResetTokenAsync(SecurityTokens.Hash(token), passwordHash, ipAddress, cancellationToken);
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Password reset rejected: the presented token is not valid. IP: {IpAddress}", ipAddress);
            return Result.Failure(InvalidResetToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error consuming a password reset token. IP: {IpAddress}", ipAddress);
            return Result.Failure(Error.Failure("AUTH_RESET_ERROR", ex.Message));
        }

        // Outside the transaction above, and best effort: the password and the refresh tokens are what
        // the reset is for, and they are already done. This only shortens the life of access tokens
        // that were already issued.
        await _accessTokens.RevokeUserAsync(userId, cancellationToken);
        _logger.LogInformation("Password reset completed for user {UserId}; all sessions revoked. IP: {IpAddress}", userId, ipAddress);
        await AnnounceAsync(new PasswordChangedEvent(userId, await RefOfAsync(userId, cancellationToken), "reset"), ipAddress, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = await _provider.ConsumeEmailVerificationTokenAsync(SecurityTokens.Hash(token), cancellationToken);
            _logger.LogInformation("Email address confirmed for user {UserId}", userId);
            await AnnounceAsync(new EmailVerifiedEvent(userId, await RefOfAsync(userId, cancellationToken)), null, cancellationToken);
            return Result.Success();
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Email verification rejected: the presented token is not valid.");
            return Result.Failure(InvalidVerificationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error consuming an email verification token.");
            return Result.Failure(Error.Failure("AUTH_VERIFICATION_ERROR", ex.Message));
        }
    }

    public async Task<Result> ResendVerificationAsync(string email, CancellationToken cancellationToken = default)
    {
        // Anonymous, and 204 on every path, for the same reason as ForgotPasswordAsync.
        //
        // Anonymous specifically because sign-in requires a verified address: an account that needs
        // this endpoint is by definition one that cannot obtain a token to call an authenticated one.
        User user;
        try
        {
            user = await _provider.GetByEmailAsync(email, cancellationToken);
        }
        catch (SecurityException)
        {
            return Result.Success();
        }

        if (user.IsEmailVerified || !user.IsActive)
        {
            return Result.Success();
        }

        // Before the token, for the same reason as ForgotPasswordAsync: the link already in the
        // owner's inbox keeps working.
        if (!await _mailCooldown.TryAcquireAsync(user.Email, MailKind.EmailVerification))
        {
            _logger.LogInformation("Email verification for user {UserId} suppressed by the mail cooldown", user.Id);
            _metrics.RecordMailSuppressed(nameof(MailKind.EmailVerification));
            return Result.Success();
        }

        try
        {
            await IssueEmailVerificationAsync(user, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reissue an email verification for user {UserId}", user.Id);
        }

        return Result.Success();
    }

    /// <summary>
    /// Verifies <paramref name="password"/> against <see cref="DummyPasswordHash"/> and discards the
    /// answer, so the paths that have no real hash to check take as long as the ones that do.
    /// </summary>
    private void SpendPasswordCheck(string password)
    {
        _passwordHasher.VerifyHashedPassword(new User(), DummyPasswordHash.Value, password);
    }

    /// <summary>
    /// Re-hashes a password the hasher reported as stored with outdated settings, so raising the work
    /// factor takes effect as people sign in rather than never. Best effort: the sign-in has already
    /// succeeded, and the old hash keeps working until the next one.
    /// </summary>
    private async Task UpgradePasswordHashAsync(User user, string password, CancellationToken cancellationToken)
    {
        try
        {
            var upgraded = await _provider.UpgradePasswordHashAsync(user.Id, user.PasswordHash, _passwordHasher.HashPassword(user, password), cancellationToken);
            _logger.LogInformation("Password hash for user {UserId} {Outcome}", user.Id, upgraded ? "upgraded to the current settings" : "changed meanwhile; not upgraded");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not upgrade the password hash for user {UserId}", user.Id);
        }
    }

    /// <summary>
    /// Counts a wrong password against the account, and raises an alert when that failure locks it.
    /// </summary>
    private async Task RecordFailedPasswordAsync(User user, string ipAddress, CancellationToken cancellationToken)
    {
        var lockedUntil = await _provider.RecordLoginFailureAsync(user.Id, MaxFailedLogins, LoginLockout, cancellationToken);
        if (lockedUntil > DateTime.UtcNow)
        {
            _logger.LogWarning("Account {UserId} locked until {LockedUntil} after {MaxFailures} wrong passwords. IP: {IpAddress}", user.Id, lockedUntil, MaxFailedLogins, ipAddress);
            await _eventBus.PublishAsync(new SecurityAlertEvent(
                "AccountLocked",
                $"User ID {user.Id} was locked after {MaxFailedLogins} wrong passwords.",
                ipAddress,
                $"UserId: {user.Id}"), cancellationToken);
        }
    }

    /// <summary>
    /// Issues a fresh verification token and queues the mail carrying it. Supersedes any token the
    /// account already has, so the most recent link is always the one that works.
    /// </summary>
    private async Task IssueEmailVerificationAsync(User user, CancellationToken cancellationToken)
    {
        var token = SecurityTokens.Generate();
        var expiresAt = DateTime.UtcNow.Add(EmailVerificationLifetime);

        await _provider.CreateEmailVerificationTokenAsync(user.Id, SecurityTokens.Hash(token), expiresAt, cancellationToken);
        await _mailer.QueueEmailVerificationAsync(user.Email, user.Username, token, expiresAt, cancellationToken);

        _logger.LogInformation("Email verification issued for user {UserId}", user.Id);
    }

    /// <summary>
    /// Answers a uniqueness conflict on registration without saying which address is taken.
    /// </summary>
    /// <remarks>
    /// An address that already has an account gets the same 204 a successful registration gets, and
    /// the fact that it is taken is delivered to the address itself rather than to whoever submitted
    /// the form. A username collision is reported plainly: it says nothing about any address, and
    /// answering 204 there would leave someone stuck retrying a name that will never be accepted.
    /// </remarks>
    /// <remarks>
    /// Internal rather than private only so the suite can exercise it directly: the alternative is
    /// fabricating a <see cref="SqlException"/>, which has no public constructor. <c>AuthService</c>
    /// is itself internal, so this widens nothing outside the assembly.
    /// </remarks>
    internal async Task<Result> HandleRegistrationConflictAsync(string username, string email, Exception ex, CancellationToken cancellationToken)
    {
        _logger.LogWarning("User registration conflict for Username={Username}. Error: {Message}", username, ex.Message);
        _logger.LogTrace(ex, "User registration conflict stack trace for {Username}", username);

        User? existing;
        try
        {
            existing = await _provider.GetByEmailAsync(email, cancellationToken);
        }
        catch (SecurityException)
        {
            // No account on this address, so the collision was on the username.
            existing = null;
        }
        catch (Exception lookupEx)
        {
            // The lookup itself failed, so which field collided is unknown. Reporting a username
            // conflict here would be a guess, and reporting 204 would tell the caller an account was
            // created when none was. This is our fault and is answered as such - the method runs
            // inside RegisterUserAsync's catch block, so returning rather than throwing keeps the
            // metric and the error shape consistent with every other failure there.
            _logger.LogError(lookupEx, "Could not determine which field collided while registering {Username}", username);
            _metrics.RecordRegistration("error");
            return Result.Failure(Error.Failure("AUTH_REGISTRATION_ERROR", lookupEx.Message));
        }

        if (existing is null)
        {
            _metrics.RecordRegistration("conflict");
            return Result.Failure(Error.Conflict("AUTH_USERNAME_CONFLICT", "That username is already taken."));
        }

        try
        {
            if (await _mailCooldown.TryAcquireAsync(existing.Email, MailKind.AccountAlreadyExists))
            {
                await _mailer.QueueAccountAlreadyExistsAsync(existing.Email, existing.Username, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Account-exists notice for user {UserId} suppressed by the mail cooldown", existing.Id);
                _metrics.RecordMailSuppressed(nameof(MailKind.AccountAlreadyExists));
            }
        }
        catch (Exception mailEx)
        {
            _logger.LogError(mailEx, "Failed to notify user {UserId} that their address was used in a registration attempt", existing.Id);
        }

        // Counted separately from "success" so the metric does not claim accounts that were never
        // created, and separately from "conflict" so this path stays visible.
        _metrics.RecordRegistration("existing_address");
        return Result.Success();
    }

    /// <summary>
    /// Confirms a token names a live invitation addressed to <paramref name="email"/>, before any
    /// account exists. Every rejection is reported identically, so an unauthenticated caller cannot
    /// use registration to discover whether a token is real or which address it was issued to.
    /// </summary>
    private async Task<Result<InvitationLookup>> ValidateInvitationAsync(string invitationToken, string email, CancellationToken cancellationToken)
    {
        InvitationLookup invitation;
        try
        {
            invitation = await _provider.GetInvitationByTokenHashAsync(SecurityTokens.Hash(invitationToken), cancellationToken);
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Registration rejected: no invitation matches the presented token");
            return Result<InvitationLookup>.Failure(InvalidInvitation);
        }

        // Ordinal-ignore-case, matching how SQL Server compares under the database's default
        // collation — so the check here agrees with the one TenantInvitation_Accept applies.
        if (!invitation.IsLive || !string.Equals(invitation.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Registration rejected: invitation {RefId} is not live, or was issued to a different address", invitation.RefId);
            return Result<InvitationLookup>.Failure(InvalidInvitation);
        }

        return Result<InvitationLookup>.Success(invitation);
    }

    private string GenerateAccessToken(User user, Guid sessionId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("type", "user"),
            new Claim(JwtServiceCollectionExtensions.JwtSessionClaim, sessionId.ToString()),
            new Claim(JwtServiceCollectionExtensions.JwtUserRefClaim, user.RefId.ToString())
        };

        return _jwtService.GenerateToken(claims, JwtAudiences.Api, _accessTokenLifetime);
    }

    private RefreshToken GenerateRefreshToken(int userId)
    {
        using var rng = RandomNumberGenerator.Create();
        var randomBytes = new byte[64];
        rng.GetBytes(randomBytes);

        // A new session id: used as-is when this token starts a sign-in, and ignored by rotation,
        // which keeps the presented token's.
        return new RefreshToken
        {
            UserId = userId,
            SessionId = Guid.NewGuid(),
            Token = Convert.ToBase64String(randomBytes),
            Expires = DateTime.UtcNow.Add(_refreshTokenLifetime),
        };
    }

    /// <summary>
    /// Publishes a change the person made to their own account, logged in each of their workspaces.
    /// Best effort: the change is made, and a failure here must not report it as failed.
    /// </summary>
    private async Task AnnounceAsync(AccountEvent @event, string? ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            await _eventBus.PublishAsync(
                @event with { WorkspaceIds = await _audit.OfUserAsync(@event.UserId, cancellationToken), ClientAddress = ipAddress },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not publish {EventType} for user {UserId} for the audit log.", @event.GetType().Name, @event.UserId);
        }
    }

    /// <summary>The user's reference, or <see cref="Guid.Empty"/> when it cannot be read; only the audit log uses it.</summary>
    private async Task<Guid> RefOfAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            return (await _provider.GetByIdAsync(userId, cancellationToken)).RefId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read user {UserId}'s reference for the audit log.", userId);
            return Guid.Empty;
        }
    }
}
