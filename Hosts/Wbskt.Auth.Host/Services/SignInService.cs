using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class SignInService : ISignInService
{
    private readonly IAccountProvider _accounts;
    private readonly ISessionProvider _sessions;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SignInService> _logger;
    private readonly AuthMetrics _metrics;
    private readonly IAccessTokenRevocation _accessTokens;
    private readonly SessionTokens _tokens;
    private readonly AccountAnnouncer _announcer;
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

    public SignInService(
        IAccountProvider accounts,
        ISessionProvider sessions,
        IJwtService jwtService,
        IEventBus eventBus,
        IOptions<AuthEmailOptions> emailOptions,
        ILogger<SignInService> logger,
        AuthMetrics metrics,
        IAccessTokenRevocation accessTokens,
        IOptions<AccessTokenOptions> accessTokenOptions,
        IAuditWorkspaces? audit = null)
    {
        _audit = audit ?? AuditWorkspaces.None;
        _accounts = accounts;
        _sessions = sessions;
        _eventBus = eventBus;
        _logger = logger;
        _metrics = metrics;
        _accessTokens = accessTokens;
        _requireVerifiedEmail = emailOptions.Value.RequireVerifiedEmailForSignIn;
        _tokens = new SessionTokens(jwtService, accessTokenOptions.Value);
        _announcer = new AccountAnnouncer(accounts, eventBus, _audit, logger);
    }

    public async Task<Result<LoginResponse>> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Attempting login from IP: {IpAddress}", ipAddress);

        try 
        {
            User user;
            try
            {
                user = await _accounts.GetByEmailAsync(email, cancellationToken);
                _logger.LogDebug("User record resolved for email {EmailHash}", EmailFingerprint.Of(email));
            }
            catch (SecurityException)
            {
                SpendPasswordCheck(password);
                _logger.LogWarning("Login failed: no user with email {EmailHash}. IP: {IpAddress}", EmailFingerprint.Of(email), ipAddress);
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
                _logger.LogWarning("Login failed: Invalid password for user {UserId}. IP: {IpAddress}", user.Id, ipAddress);
                _metrics.RecordLogin("invalid_credentials");
                await AccountLockout.RecordFailedPasswordAsync(_accounts, _eventBus, _logger, user, ipAddress, cancellationToken);
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "Invalid password") { WorkspaceIds = await _audit.OfUserAsync(user.Id, cancellationToken) }, cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid credentials."));
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed: Account is inactive for user {UserId}. IP: {IpAddress}", user.Id, ipAddress);
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
                _logger.LogWarning("Login refused: address not yet verified for user {UserId}. IP: {IpAddress}", user.Id, ipAddress);
                _metrics.RecordLogin("email_unverified");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "Email not verified") { WorkspaceIds = await _audit.OfUserAsync(user.Id, cancellationToken) }, cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized(
                    "AUTH_EMAIL_UNVERIFIED",
                    "Confirm your email address before signing in. Check your inbox, or ask for a new confirmation link."));
            }

            await _accounts.RecordLoginSuccessAsync(user.Id, cancellationToken);

            if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                await UpgradePasswordHashAsync(user, password, cancellationToken);
            }

            _logger.LogDebug("Credentials verified for user {UserId}. Generating tokens...", user.Id);
            var refreshToken = _tokens.NewRefreshToken(user.Id);
            var accessToken = _tokens.AccessToken(user, refreshToken.SessionId);

            await _sessions.InsertRefreshTokenAsync(refreshToken, ipAddress, cancellationToken);
            _logger.LogDebug("Refresh token inserted for user ID: {UserId}", user.Id);

            await _eventBus.PublishAsync(new UserLoginSuccessEvent(user.Id, user.RefId, ipAddress) { WorkspaceIds = await _audit.OfUserAsync(user.Id, cancellationToken) }, cancellationToken);
            _logger.LogInformation("User {Username} logged in successfully. IP: {IpAddress}", user.Username, ipAddress);

            _metrics.RecordLogin("success");
            return Result<LoginResponse>.Success(new LoginResponse(accessToken, refreshToken.Token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordLogin("error");
            // Event logs are readable by tenant admins, so the alert names the fault's type, not its
            // message (which can quote the address or SQL detail), and a fingerprint, not the address.
            var emailHash = EmailFingerprint.Of(email);
            await _eventBus.PublishAsync(new SecurityAlertEvent(
                "LoginFailure",
                $"Unexpected {ex.GetType().Name} during login for email {emailHash}.",
                ipAddress,
                $"EmailHash: {emailHash}"), cancellationToken);

            // Not a bare rethrow: GlobalExceptionMiddleware logs the message and publishes it in a
            // SystemErrorEvent, so the fault goes on as its type name and stack frames only.
            throw SignInFaultException.From(ex);
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
            var newRefreshToken = _tokens.NewRefreshToken(0);
            var rotation = await _sessions.RotateRefreshTokenAsync(token, newRefreshToken, _tokens.SessionLifetime, ipAddress, cancellationToken);

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
                    await RevokeAccessTokensAsync(userId, cancellationToken);
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
            var newAccessToken = _tokens.AccessToken(user, rotation.SessionId!.Value);

            // Queued, not awaited on the broker (QueuedEventBus): the rotation is committed, and an
            // event that fails to go out must not turn it into an error the client acts on.
            await _eventBus.PublishAsync(new TokenRotatedEvent(user.Id, user.RefId, ipAddress), cancellationToken);

            // Debug: the most frequent call this host serves. AuthMetrics counts the outcomes.
            _logger.LogDebug("Token refreshed for user {UserId}. IP: {IpAddress}", user.Id, ipAddress);
            _metrics.RecordRefresh("success");
            return Result<LoginResponse>.Success(new LoginResponse(newAccessToken, newRefreshToken.Token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordRefresh("error");
            throw;
        }
    }

    public async Task<Result> LogoutAsync(string token, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Attempting logout from IP: {IpAddress}", ipAddress);

        // Deliberately not reporting whether the token existed: logout is unauthenticated by
        // necessity, and a distinguishable response would turn it into a token oracle.
        var sessionId = await _sessions.RevokeRefreshTokenAsync(token, ipAddress, null, cancellationToken);
        if (sessionId is not null)
        {
            // The refresh token is gone, so this ends the access token the device still holds.
            await _accessTokens.RevokeSessionAsync(sessionId.Value, cancellationToken);
        }

        _logger.LogInformation("Logout {Outcome}. IP: {IpAddress}", sessionId is null ? "found no live token" : "ended a session", ipAddress);
        return Result.Success();
    }

    public async Task<Result> LogoutAllAsync(int userId, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to revoke all sessions for user ID: {UserId} from IP: {IpAddress}", userId, ipAddress);

        var revoked = await _sessions.RevokeAllRefreshTokensForUserAsync(userId, ipAddress, cancellationToken);
        await RevokeAccessTokensAsync(userId, cancellationToken);
        _logger.LogInformation("Revoked {RevokedCount} refresh token(s) for user ID: {UserId}", revoked, userId);
        await _announcer.AnnounceAsync(new SessionsRevokedEvent(userId, await _announcer.RefOfAsync(userId, cancellationToken), "all"), ipAddress, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyCollection<SessionResponse>>> GetSessionsAsync(int userId, Guid? currentSessionId, CancellationToken cancellationToken = default)
    {
        var sessions = await _sessions.GetActiveSessionsAsync(userId, cancellationToken);
        return Result<IReadOnlyCollection<SessionResponse>>.Success(
            sessions.Select(s => s with { IsCurrent = s.Id == currentSessionId }).ToList());
    }

    public async Task<Result> RevokeSessionAsync(int userId, Guid sessionId, string ipAddress, CancellationToken cancellationToken = default)
    {
        var revoked = await _sessions.RevokeSessionAsync(sessionId, userId, ipAddress, cancellationToken);
        if (revoked == 0)
        {
            // Someone else's session reads exactly like one that does not exist.
            return Result.Failure(Error.NotFound("AUTH_SESSION_NOT_FOUND", "Session not found."));
        }

        // After the refresh token, so the device cannot mint an access token the revocation misses.
        await _accessTokens.RevokeSessionAsync(sessionId, cancellationToken);

        _logger.LogInformation("User {UserId} ended session {SessionId}. IP: {IpAddress}", userId, sessionId, ipAddress);
        await _announcer.AnnounceAsync(new SessionsRevokedEvent(userId, await _announcer.RefOfAsync(userId, cancellationToken), "one"), ipAddress, cancellationToken);
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
            var upgraded = await _accounts.UpgradePasswordHashAsync(user.Id, user.PasswordHash, _passwordHasher.HashPassword(user, password), cancellationToken);
            _logger.LogInformation("Password hash for user {UserId} {Outcome}", user.Id, upgraded ? "upgraded to the current settings" : "changed meanwhile; not upgraded");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not upgrade the password hash for user {UserId}", user.Id);
        }
    }

    /// <summary>
    /// Ends the user's access tokens on every host. Revocations are keyed by the RefId a token names;
    /// unlike <see cref="AccountAnnouncer.RefOfAsync"/> a failed read is not swallowed, since a
    /// revocation that silently named nobody would leave the tokens working.
    /// </summary>
    private async Task RevokeAccessTokensAsync(int userId, CancellationToken cancellationToken) =>
        await _accessTokens.RevokeUserAsync((await _accounts.GetByIdAsync(userId, cancellationToken)).RefId, cancellationToken);
}
