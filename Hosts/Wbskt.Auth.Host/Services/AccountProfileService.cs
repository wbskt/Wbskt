using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class AccountProfileService : IAccountProfileService
{
    public static readonly TimeSpan UsernameChangeInterval = TimeSpan.FromDays(30);

    /// <summary>A day, like a sign-up confirmation: the new inbox may be one the person checks later.</summary>
    public static readonly TimeSpan EmailChangeLifetime = TimeSpan.FromHours(24);

    public static readonly TimeSpan EmailRevertLifetime = TimeSpan.FromDays(7);

    public static readonly Error UsernameReserved =
        Error.Validation("USERNAME_RESERVED", "That username is reserved. Choose another.");

    public static readonly Error UsernameTaken =
        Error.Conflict("USERNAME_TAKEN", "That username is taken. Choose another.");

    /// <summary>
    /// One answer for an address that belongs to another account, at the request and when a link is
    /// followed. It says the address cannot be used, not why.
    /// </summary>
    public static readonly Error EmailUnavailable =
        Error.Conflict("EMAIL_CHANGE_CONFLICT", "That address cannot be used for this account.");

    public static readonly Error EmailUnchanged =
        Error.Validation("EMAIL_UNCHANGED", "That is already this account's address.");

    public static readonly Error InvalidEmailChangeToken =
        Error.Validation("EMAIL_CHANGE_TOKEN_INVALID", "This link is not valid. It may have expired or already been used.");

    private readonly IAccountProvider _accounts;
    private readonly ISessionProvider _sessions;
    private readonly IEventBus _eventBus;
    private readonly IAuthMailer _mailer;
    private readonly MailCooldown _mailCooldown;
    private readonly IAccessTokenRevocation _accessTokens;
    private readonly ILogger<AccountProfileService> _logger;
    private readonly AuthMetrics _metrics;
    private readonly SessionTokens _tokens;
    private readonly AccountAnnouncer _announcer;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AccountProfileService(
        IAccountProvider accounts,
        ISessionProvider sessions,
        IJwtService jwtService,
        IEventBus eventBus,
        IAuthMailer mailer,
        MailCooldown mailCooldown,
        IAccessTokenRevocation accessTokens,
        IOptions<AccessTokenOptions> accessTokenOptions,
        ILogger<AccountProfileService> logger,
        AuthMetrics metrics,
        IAuditWorkspaces? audit = null)
    {
        _accounts = accounts;
        _sessions = sessions;
        _eventBus = eventBus;
        _mailer = mailer;
        _mailCooldown = mailCooldown;
        _accessTokens = accessTokens;
        _logger = logger;
        _metrics = metrics;
        _tokens = new SessionTokens(jwtService, accessTokenOptions.Value);
        _announcer = new AccountAnnouncer(accounts, eventBus, audit ?? AuditWorkspaces.None, logger);
    }

    public async Task<Result<LoginResponse>> ChangeUsernameAsync(int userId, Guid? sessionId, string username, string ipAddress, CancellationToken cancellationToken = default)
    {
        if (ReservedUsernames.IsReserved(username))
        {
            return Result<LoginResponse>.Failure(UsernameReserved);
        }

        User before;
        try
        {
            before = await _accounts.GetByIdAsync(userId, cancellationToken);
            await _accounts.ChangeUsernameAsync(userId, username, UsernameChangeInterval, cancellationToken);
        }
        catch (SecurityException)
        {
            return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_NOT_FOUND", "User not found."));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Result<LoginResponse>.Failure(UsernameTaken);
        }
        catch (SqlException ex) when (ex.Number == 50022)
        {
            return Result<LoginResponse>.Failure(Error.Validation("USERNAME_CHANGED_RECENTLY", ex.Message));
        }

        // Read back rather than patched: the access token carries what the database holds.
        var user = await _accounts.GetByIdAsync(userId, cancellationToken);
        if (!string.Equals(before.Username, user.Username, StringComparison.Ordinal))
        {
            _logger.LogInformation("User {UserId} changed their username. IP: {IpAddress}", userId, ipAddress);
            await _announcer.AnnounceAsync(new UserProfileChangedEvent(userId, user.RefId, "username", before.Username, user.Username, "changed"), ipAddress, cancellationToken);
        }

        // Sessions from before tokens carried a session id get a token for an unknown one, which
        // works until the next refresh replaces it; nothing revokes by that id.
        return Result<LoginResponse>.Success(new LoginResponse(_tokens.AccessToken(user, sessionId ?? Guid.Empty), null));
    }

    public async Task<Result> RequestEmailChangeAsync(int userId, string password, string newEmail, string ipAddress, CancellationToken cancellationToken = default)
    {
        User user;
        try
        {
            user = await _accounts.GetByIdAsync(userId, cancellationToken);
        }
        catch (SecurityException)
        {
            return Result.Failure(Error.Unauthorized("AUTH_USER_NOT_FOUND", "User not found."));
        }

        // Same rules as changing the password: a stolen access token must not be enough to point the
        // account at another mailbox, nor an unthrottled way to guess the password behind it.
        if (user.LockedUntil > DateTime.UtcNow)
        {
            return Result.Failure(Error.Forbidden("AUTH_ACCOUNT_LOCKED", "Too many wrong passwords. Try again later, or reset your password."));
        }

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Email change refused: the password did not match for user {UserId}. IP: {IpAddress}", userId, ipAddress);
            await AccountLockout.RecordFailedPasswordAsync(_accounts, _eventBus, _logger, user, ipAddress, cancellationToken);
            return Result.Failure(Error.Validation("AUTH_CURRENT_PASSWORD_INVALID", "The current password is not correct."));
        }

        if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(EmailUnchanged);
        }

        if (await IsTakenAsync(newEmail, cancellationToken))
        {
            _logger.LogInformation("Email change for user {UserId} refused: the new address is in use. IP: {IpAddress}", userId, ipAddress);
            return Result.Failure(EmailUnavailable);
        }

        // Before the token, as for a reset: a new token replaces the pending one, and replacing it
        // without a mail would leave the person holding a dead link.
        if (!await _mailCooldown.TryAcquireAsync(newEmail, MailKind.EmailChange))
        {
            _logger.LogInformation("Email change for user {UserId} suppressed by the mail cooldown. IP: {IpAddress}", userId, ipAddress);
            _metrics.RecordMailSuppressed(nameof(MailKind.EmailChange));
            return Result.Success();
        }

        var token = SecurityTokens.Generate();
        var expiresAt = DateTime.UtcNow.Add(EmailChangeLifetime);
        await _accounts.CreateEmailChangeTokenAsync(userId, newEmail, SecurityTokens.Hash(token), expiresAt, ipAddress, cancellationToken);
        await _mailer.QueueEmailChangeAsync(newEmail, user.Username, token, expiresAt, cancellationToken);

        _logger.LogInformation("Email change requested for user {UserId}. IP: {IpAddress}", userId, ipAddress);
        return Result.Success();
    }

    public async Task<Result<LoginResponse?>> ConfirmEmailChangeAsync(string token, int? callerUserId, string ipAddress, CancellationToken cancellationToken = default)
    {
        var revertToken = SecurityTokens.Generate();
        var revertExpiresAt = DateTime.UtcNow.Add(EmailRevertLifetime);

        EmailChange change;
        try
        {
            change = await _accounts.ConfirmEmailChangeAsync(SecurityTokens.Hash(token), SecurityTokens.Hash(revertToken), revertExpiresAt, ipAddress, cancellationToken);
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Email change rejected: the presented token is not valid. IP: {IpAddress}", ipAddress);
            return Result<LoginResponse?>.Failure(InvalidEmailChangeToken);
        }
        catch (SqlException ex) when (ex.Number is 50021 or 2601 or 2627)
        {
            return Result<LoginResponse?>.Failure(EmailUnavailable);
        }

        var user = await _accounts.GetByIdAsync(change.UserId, cancellationToken);
        await _accessTokens.RevokeUserAsync(user.RefId, cancellationToken);
        await _mailer.QueueEmailChangedAsync(change.From, user.Username, change.To, revertToken, revertExpiresAt, cancellationToken);

        _logger.LogInformation("Email changed for user {UserId}; all sessions revoked. IP: {IpAddress}", change.UserId, ipAddress);
        await _announcer.AnnounceAsync(new UserProfileChangedEvent(change.UserId, user.RefId, "email", change.From, change.To, "changed"), ipAddress, cancellationToken);

        if (callerUserId != change.UserId)
        {
            return Result<LoginResponse?>.Success(null);
        }

        var refreshToken = _tokens.NewRefreshToken(change.UserId);
        await _sessions.InsertRefreshTokenAsync(refreshToken, ipAddress, cancellationToken);
        return Result<LoginResponse?>.Success(new LoginResponse(_tokens.AccessToken(user, refreshToken.SessionId), refreshToken.Token));
    }

    public async Task<Result> RevertEmailChangeAsync(string token, string ipAddress, CancellationToken cancellationToken = default)
    {
        EmailChange change;
        try
        {
            change = await _accounts.RevertEmailChangeAsync(SecurityTokens.Hash(token), ipAddress, cancellationToken);
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Email change revert rejected: the presented token is not valid. IP: {IpAddress}", ipAddress);
            return Result.Failure(InvalidEmailChangeToken);
        }
        catch (SqlException ex) when (ex.Number is 50021 or 2601 or 2627)
        {
            return Result.Failure(EmailUnavailable);
        }

        // Not RefOfAsync: a revocation that silently named nobody would leave the tokens working.
        var userRef = (await _accounts.GetByIdAsync(change.UserId, cancellationToken)).RefId;
        await _accessTokens.RevokeUserAsync(userRef, cancellationToken);

        _logger.LogWarning("Email change reverted for user {UserId}; all sessions revoked. IP: {IpAddress}", change.UserId, ipAddress);
        await _announcer.AnnounceAsync(new UserProfileChangedEvent(change.UserId, userRef, "email", change.From, change.To, "reverted"), ipAddress, cancellationToken);
        return Result.Success();
    }

    private async Task<bool> IsTakenAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            await _accounts.GetByEmailAsync(email, cancellationToken);
            return true;
        }
        catch (SecurityException)
        {
            return false;
        }
    }
}
