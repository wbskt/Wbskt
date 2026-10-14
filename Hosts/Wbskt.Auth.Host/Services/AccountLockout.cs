using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Per-account lockout: every wrong password counts, wherever it is typed - signing in, or confirming
/// the current password before changing it.
/// </summary>
internal static class AccountLockout
{
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

    /// <summary>
    /// Counts a wrong password against the account, and raises an alert when that failure locks it.
    /// </summary>
    public static async Task RecordFailedPasswordAsync(IAccountProvider accounts, IEventBus eventBus, ILogger logger, User user, string ipAddress, CancellationToken cancellationToken)
    {
        var lockedUntil = await accounts.RecordLoginFailureAsync(user.Id, MaxFailedLogins, LoginLockout, cancellationToken);
        if (lockedUntil > DateTime.UtcNow)
        {
            logger.LogWarning("Account {UserId} locked until {LockedUntil} after {MaxFailures} wrong passwords. IP: {IpAddress}", user.Id, lockedUntil, MaxFailedLogins, ipAddress);
            await eventBus.PublishAsync(new SecurityAlertEvent(
                "AccountLocked",
                $"User ID {user.Id} was locked after {MaxFailedLogins} wrong passwords.",
                ipAddress,
                $"UserId: {user.Id}"), cancellationToken);
        }
    }
}
