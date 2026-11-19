using Microsoft.AspNetCore.Identity;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Passwords;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Checks an account's password under the lockout, wherever one is typed: signing in, and entering it
/// again before changing the password or the address, or deleting the account.
/// </summary>
/// <remarks>
/// An attempt is reserved before the password is checked and settled after
/// (<see cref="IAccountProvider.BeginPasswordCheckAsync"/>), so parallel guesses cannot all pass the
/// lock check before any of them is counted. The lock is compared in the database, by the clock that
/// set it.
/// </remarks>
internal sealed class AccountCredentials(
    IAccountProvider accounts,
    ISessionProvider sessions,
    PasswordHashing hashing,
    IEventBus eventBus,
    SecurityNotices notices,
    IAccessTokenRevocation accessTokens,
    ILogger<AccountCredentials> logger)
{
    /// <summary>
    /// For a caller signed in as the account, so saying it is locked discloses nothing, and checking the
    /// password while locked would reopen the guessing the lock closed.
    /// </summary>
    public static readonly Error Locked =
        Error.Forbidden("AUTH_ACCOUNT_LOCKED", "Too many wrong passwords. Try again later, or reset your password.");

    /// <summary>400, not 401: the session is fine, and a 401 would make a client sign the user out.</summary>
    public static readonly Error WrongPassword =
        Error.Validation("AUTH_CURRENT_PASSWORD_INVALID", "The current password is not correct.");

    public static readonly Error UserNotFound =
        Error.Unauthorized("AUTH_USER_NOT_FOUND", "User not found.");

    /// <summary>
    /// Checks a sign-in's password. A locked account is not tested, unless
    /// <paramref name="deviceToken"/> names a browser it signed in from before, so a stranger's guesses
    /// cannot keep the owner out. Either way the hashing work is spent, so the answer takes as long.
    /// </summary>
    public async Task<PasswordCheck> CheckAsync(User user, string password, string ipAddress, string? deviceToken, CancellationToken cancellationToken)
    {
        var reserved = await accounts.BeginPasswordCheckAsync(user.Id, AccountLockout.MaxFailedLogins, throughLock: false, cancellationToken)
            || (await IsKnownDeviceAsync(user.Id, deviceToken, cancellationToken)
                && await accounts.BeginPasswordCheckAsync(user.Id, AccountLockout.MaxFailedLogins, throughLock: true, cancellationToken));

        if (!reserved)
        {
            // Against a dummy hash, not the account's: the work is spent, the password is not tested.
            await hashing.SpendAsync(password, cancellationToken);
            return PasswordCheck.Locked;
        }

        var result = await hashing.VerifyAsync(user, password, cancellationToken);
        if (result == PasswordVerificationResult.Failed)
        {
            await RecordWrongPasswordAsync(user, ipAddress, cancellationToken);
            return PasswordCheck.Wrong;
        }

        await accounts.EndPasswordCheckAsync(user.Id, succeeded: true, AccountLockout.MaxFailedLogins, AccountLockout.LoginLockout, cancellationToken);
        return result == PasswordVerificationResult.SuccessRehashNeeded ? PasswordCheck.VerifiedNeedsRehash : PasswordCheck.Verified;
    }

    /// <summary>
    /// The password entered again by someone signed in as the account, before a change a stolen access
    /// token must not be enough for. A wrong one counts towards the lock like a failed sign-in.
    /// </summary>
    public async Task<Result<User>> ConfirmPasswordAsync(int userId, string password, string ipAddress, CancellationToken cancellationToken)
    {
        var user = await accounts.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result<User>.Failure(UserNotFound);
        }

        var confirmed = await ConfirmPasswordAsync(user, password, ipAddress, cancellationToken);
        return confirmed.IsSuccess ? Result<User>.Success(user) : Result<User>.Failure(confirmed.Error);
    }

    /// <inheritdoc cref="ConfirmPasswordAsync(int, string, string, CancellationToken)"/>
    public async Task<Result> ConfirmPasswordAsync(User user, string password, string ipAddress, CancellationToken cancellationToken)
    {
        switch (await CheckAsync(user, password, ipAddress, deviceToken: null, cancellationToken))
        {
            case PasswordCheck.Locked:
                logger.LogWarning("Password confirmation refused: account {UserId} is locked. IP: {IpAddress}", user.Id, ipAddress);
                return Result.Failure(Locked);

            case PasswordCheck.Wrong:
                logger.LogWarning("Password confirmation refused: the password did not match for user {UserId}. IP: {IpAddress}", user.Id, ipAddress);
                return Result.Failure(WrongPassword);

            default:
                return Result.Success();
        }
    }

    /// <summary>
    /// Ends the user's access tokens on every host. Revocations are keyed by the reference a token names.
    /// </summary>
    public async Task RevokeAccessTokensAsync(Guid userRef, CancellationToken cancellationToken) =>
        await accessTokens.RevokeUserAsync(userRef, cancellationToken);

    private async Task<bool> IsKnownDeviceAsync(int userId, string? deviceToken, CancellationToken cancellationToken) =>
        !string.IsNullOrEmpty(deviceToken) && await sessions.IsKnownDeviceAsync(userId, deviceToken, cancellationToken);

    /// <summary>
    /// Leaves the attempt counted, and raises an alert and mails the owner when it locked the account.
    /// </summary>
    private async Task RecordWrongPasswordAsync(User user, string ipAddress, CancellationToken cancellationToken)
    {
        var lockedUntil = await accounts.EndPasswordCheckAsync(user.Id, succeeded: false, AccountLockout.MaxFailedLogins, AccountLockout.LoginLockout, cancellationToken);
        if (lockedUntil is not { } until)
        {
            return;
        }

        logger.LogWarning("Account {UserId} locked until {LockedUntil} after {MaxFailures} wrong passwords. IP: {IpAddress}", user.Id, until, AccountLockout.MaxFailedLogins, ipAddress);
        await eventBus.PublishAsync(new SecurityAlertEvent(
            "AccountLocked",
            $"User ID {user.Id} was locked after {AccountLockout.MaxFailedLogins} wrong passwords.",
            ipAddress,
            $"UserId: {user.Id}"), cancellationToken);
        await notices.AccountLockedAsync(user, until, ipAddress, cancellationToken);
    }
}
