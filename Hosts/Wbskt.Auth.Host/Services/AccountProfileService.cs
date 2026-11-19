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
    private readonly IAuthMailer _mailer;
    private readonly MailCooldown _mailCooldown;
    private readonly ILogger<AccountProfileService> _logger;
    private readonly AuthMetrics _metrics;
    private readonly SessionTokens _tokens;
    private readonly AccountAnnouncer _announcer;
    private readonly AccountCredentials _credentials;
    private readonly TimeProvider _time;

    public AccountProfileService(
        IAccountProvider accounts,
        ISessionProvider sessions,
        IAuthMailer mailer,
        MailCooldown mailCooldown,
        SessionTokens tokens,
        ILogger<AccountProfileService> logger,
        AuthMetrics metrics,
        AccountCredentials credentials,
        AccountAnnouncer announcer,
        TimeProvider time)
    {
        _accounts = accounts;
        _sessions = sessions;
        _mailer = mailer;
        _mailCooldown = mailCooldown;
        _logger = logger;
        _metrics = metrics;
        _tokens = tokens;
        _credentials = credentials;
        _announcer = announcer;
        _time = time;
    }

    public async Task<Result<LoginResponse>> ChangeUsernameAsync(int userId, Guid? sessionId, string username, string ipAddress, CancellationToken cancellationToken = default)
    {
        if (ReservedUsernames.IsReserved(username))
        {
            return Result<LoginResponse>.Failure(UsernameReserved);
        }

        var user = await _accounts.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result<LoginResponse>.Failure(AccountCredentials.UserNotFound);
        }

        UsernameChange change;
        try
        {
            change = await _accounts.ChangeUsernameAsync(userId, username, UsernameChangeInterval, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Result<LoginResponse>.Failure(UsernameTaken);
        }
        catch (SqlException ex) when (ex.Number == 50022)
        {
            return Result<LoginResponse>.Failure(Error.Validation("USERNAME_CHANGED_RECENTLY", ex.Message));
        }

        if (!string.Equals(change.From, change.To, StringComparison.Ordinal))
        {
            _logger.LogInformation("User {UserId} changed their username. IP: {IpAddress}", userId, ipAddress);
            await _announcer.AnnounceAsync(new UserProfileChangedEvent(userId, user.RefId, "username", change.From, change.To, "changed"), ipAddress, cancellationToken);
        }

        // Sessions from before tokens carried a session id get a token for an unknown one, which
        // works until the next refresh replaces it; nothing revokes by that id.
        // The access token carries the name the database now holds.
        var renamed = new User { Id = user.Id, RefId = user.RefId, Username = change.To, Email = user.Email };
        return Result<LoginResponse>.Success(new LoginResponse(_tokens.AccessToken(renamed, sessionId ?? Guid.Empty), null));
    }

    public async Task<Result> RequestEmailChangeAsync(int userId, string password, string newEmail, string ipAddress, CancellationToken cancellationToken = default)
    {
        // Same rules as changing the password: a stolen access token must not be enough to point the
        // account at another mailbox, nor an unthrottled way to guess the password behind it.
        var confirmed = await _credentials.ConfirmPasswordAsync(userId, password, ipAddress, cancellationToken);
        if (confirmed.IsFailure)
        {
            return Result.Failure(confirmed.Error);
        }

        var user = confirmed.Value;
        if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(EmailUnchanged);
        }

        // Before the token, as for a reset: a new token replaces the pending one, and replacing it
        // without a mail would leave the person holding a dead link. Before the lookup too, and the
        // same kind either way, so the cooldown answers alike for a taken address and a free one.
        if (!await _mailCooldown.TryAcquireAsync(newEmail, MailKind.EmailChange))
        {
            _logger.LogInformation("Email change for user {UserId} suppressed by the mail cooldown. IP: {IpAddress}", userId, ipAddress);
            _metrics.RecordMailSuppressed(nameof(MailKind.EmailChange));
            return Result.Success();
        }

        // A taken address answers 204 like a free one, as registration does, or this would tell any
        // signed-in caller which addresses have accounts. Its owner is told instead, and confirming
        // (which needs that mailbox) is still refused with EMAIL_CHANGE_CONFLICT.
        if (await _accounts.FindByEmailAsync(newEmail, cancellationToken) is { } owner)
        {
            _logger.LogInformation("Email change for user {UserId} not started: the new address is in use; its owner was told. IP: {IpAddress}", userId, ipAddress);
            await _mailer.QueueEmailChangeConflictAsync(newEmail, owner.Username, cancellationToken);
            return Result.Success();
        }

        var token = SecurityTokens.Generate();
        var expiresAt = _time.GetUtcNow().UtcDateTime.Add(EmailChangeLifetime);
        await _accounts.CreateEmailChangeTokenAsync(userId, newEmail, SecurityTokens.Hash(token), expiresAt, ipAddress, cancellationToken);
        await _mailer.QueueEmailChangeAsync(newEmail, user.Username, token, expiresAt, cancellationToken);

        _logger.LogInformation("Email change requested for user {UserId}. IP: {IpAddress}", userId, ipAddress);
        return Result.Success();
    }

    public async Task<Result<LoginResponse?>> ConfirmEmailChangeAsync(string token, int? callerUserId, string ipAddress, CancellationToken cancellationToken = default)
    {
        var revertToken = SecurityTokens.Generate();
        var revertExpiresAt = _time.GetUtcNow().UtcDateTime.Add(EmailRevertLifetime);

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

        await _credentials.RevokeAccessTokensAsync(change.UserRefId, cancellationToken);
        await _mailer.QueueEmailChangedAsync(change.From, change.Username, change.To, revertToken, revertExpiresAt, cancellationToken);

        _logger.LogInformation("Email changed for user {UserId}; all sessions revoked. IP: {IpAddress}", change.UserId, ipAddress);
        await _announcer.AnnounceAsync(new UserProfileChangedEvent(change.UserId, change.UserRefId, "email", change.From, change.To, "changed"), ipAddress, cancellationToken);

        if (callerUserId != change.UserId)
        {
            return Result<LoginResponse?>.Success(null);
        }

        var refreshToken = _tokens.NewRefreshToken(change.UserId);
        await _sessions.InsertRefreshTokenAsync(refreshToken, ipAddress, cancellationToken);
        var user = new User { Id = change.UserId, RefId = change.UserRefId, Username = change.Username, Email = change.To };
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

        await _credentials.RevokeAccessTokensAsync(change.UserRefId, cancellationToken);

        _logger.LogWarning("Email change reverted for user {UserId}; all sessions revoked. IP: {IpAddress}", change.UserId, ipAddress);
        await _announcer.AnnounceAsync(new UserProfileChangedEvent(change.UserId, change.UserRefId, "email", change.From, change.To, "reverted"), ipAddress, cancellationToken);
        return Result.Success();
    }
}
