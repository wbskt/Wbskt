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

internal sealed class PasswordService : IPasswordService
{
    /// <summary>
    /// One answer for an unknown, spent or expired recovery token, matching how the procedure that
    /// consumes one reports all three. Anything more specific says whether a token ever existed.
    /// </summary>
    private static readonly Error InvalidResetToken =
        Error.Validation("RESET_TOKEN_INVALID", "This password reset link is not valid. It may have expired or already been used. Request a new one.");

    /// <summary>
    /// Short, because a reset link in an unattended inbox is a live credential for the account. An
    /// hour is long enough to survive a user reading mail on another device.
    /// </summary>
    private static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);

    private readonly IAccountProvider _accounts;
    private readonly ISessionProvider _sessions;
    private readonly IEventBus _eventBus;
    private readonly IAuthMailer _mailer;
    private readonly MailCooldown _mailCooldown;
    private readonly ILogger<PasswordService> _logger;
    private readonly AuthMetrics _metrics;
    private readonly IAccessTokenRevocation _accessTokens;
    private readonly SessionTokens _tokens;
    private readonly AccountAnnouncer _announcer;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public PasswordService(
        IAccountProvider accounts,
        ISessionProvider sessions,
        IJwtService jwtService,
        IEventBus eventBus,
        IAuthMailer mailer,
        ILogger<PasswordService> logger,
        AuthMetrics metrics,
        IAccessTokenRevocation accessTokens,
        IOptions<AccessTokenOptions> accessTokenOptions,
        MailCooldown mailCooldown,
        IAuditWorkspaces? audit = null)
    {
        _accounts = accounts;
        _sessions = sessions;
        _eventBus = eventBus;
        _mailer = mailer;
        _mailCooldown = mailCooldown;
        _logger = logger;
        _metrics = metrics;
        _accessTokens = accessTokens;
        _tokens = new SessionTokens(jwtService, accessTokenOptions.Value);
        _announcer = new AccountAnnouncer(accounts, eventBus, audit ?? AuditWorkspaces.None, logger);
    }

    public async Task<Result<LoginResponse>> ChangePasswordAsync(int userId, string currentPassword, string newPassword, string ipAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _accounts.GetByIdAsync(userId, cancellationToken);

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
                await AccountLockout.RecordFailedPasswordAsync(_accounts, _eventBus, _logger, user, ipAddress, cancellationToken);
                // 400, not 401: the session is fine, and a 401 would make a client sign the user out.
                return Result<LoginResponse>.Failure(Error.Validation("AUTH_CURRENT_PASSWORD_INVALID", "The current password is not correct."));
            }

            await _accounts.ChangePasswordAsync(userId, _passwordHasher.HashPassword(user, newPassword), ipAddress, cancellationToken);
            await RevokeAccessTokensAsync(userId, cancellationToken);

            // Every session ended with the old password, including this one. The caller gets a new
            // pair so changing a password does not also sign them out.
            var refreshToken = _tokens.NewRefreshToken(userId);
            await _sessions.InsertRefreshTokenAsync(refreshToken, ipAddress, cancellationToken);

            _logger.LogInformation("Password changed for user {UserId}; all other sessions revoked. IP: {IpAddress}", userId, ipAddress);
            await _announcer.AnnounceAsync(new PasswordChangedEvent(userId, user.RefId, "changed"), ipAddress, cancellationToken);
            return Result<LoginResponse>.Success(new LoginResponse(_tokens.AccessToken(user, refreshToken.SessionId), refreshToken.Token));
        }
        catch (SecurityException)
        {
            return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_NOT_FOUND", "User not found."));
        }
    }

    public async Task<Result> ForgotPasswordAsync(string email, string ipAddress, CancellationToken cancellationToken = default)
    {
        // 204 on every path below. Whether an address has an account is exactly what an attacker
        // wants from this endpoint, so the status, the body and the absence of a body are identical
        // for a real address, an unknown one, and a deactivated one.
        //
        // Timing is not equalised. The hit path does one extra insert, a workspace lookup and a publish
        // for the audit log, a few milliseconds against a lookup that both paths pay - a signal, but a
        // far smaller one than a different response, and closing it properly means doing fake work
        // rather than less. Recorded rather than fixed.
        User user;
        try
        {
            user = await _accounts.GetByEmailAsync(email, cancellationToken);
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

            await _accounts.CreatePasswordResetTokenAsync(user.Id, SecurityTokens.Hash(token), expiresAt, ipAddress, cancellationToken);
            await _mailer.QueuePasswordResetAsync(user.Email, user.Username, token, expiresAt, cancellationToken);

            // The raw token is not logged here or anywhere else.
            _logger.LogInformation("Password reset issued for user {UserId}. IP: {IpAddress}", user.Id, ipAddress);
            await _announcer.AnnounceAsync(new PasswordResetRequestedEvent(user.Id, user.RefId), ipAddress, cancellationToken);
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
            userId = await _accounts.ConsumePasswordResetTokenAsync(SecurityTokens.Hash(token), passwordHash, ipAddress, cancellationToken);
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Password reset rejected: the presented token is not valid. IP: {IpAddress}", ipAddress);
            return Result.Failure(InvalidResetToken);
        }

        // Outside the transaction above, and best effort: the password and the refresh tokens are what
        // the reset is for, and they are already done. This only shortens the life of access tokens
        // that were already issued.
        await RevokeAccessTokensAsync(userId, cancellationToken);
        _logger.LogInformation("Password reset completed for user {UserId}; all sessions revoked. IP: {IpAddress}", userId, ipAddress);
        await _announcer.AnnounceAsync(new PasswordChangedEvent(userId, await _announcer.RefOfAsync(userId, cancellationToken), "reset"), ipAddress, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Ends the user's access tokens on every host. Revocations are keyed by the RefId a token names;
    /// unlike <see cref="AccountAnnouncer.RefOfAsync"/> a failed read is not swallowed, since a
    /// revocation that silently named nobody would leave the tokens working.
    /// </summary>
    private async Task RevokeAccessTokensAsync(int userId, CancellationToken cancellationToken) =>
        await _accessTokens.RevokeUserAsync((await _accounts.GetByIdAsync(userId, cancellationToken)).RefId, cancellationToken);
}
