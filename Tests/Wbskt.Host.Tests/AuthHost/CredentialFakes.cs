using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Passwords;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The password check as the host wires it, over provider mocks whose lock check reads the mocked
/// account's <see cref="User.LockedUntil"/>, as <c>User_BeginPasswordCheck</c> reads the row's.
/// </summary>
internal static class CredentialFakes
{
    public static PasswordHashing Hashing() => new(new PasswordHasher<User>(), new AuthMetrics(), concurrency: 4);

    /// <summary>
    /// Has <see cref="IAccountProvider.BeginPasswordCheckAsync"/> refuse while the account the mock
    /// returns for the id is locked, unless the check goes through the lock.
    /// </summary>
    public static void ReadLockFromUser(Mock<IAccountProvider> accounts)
    {
        accounts
            .Setup(p => p.BeginPasswordCheckAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<int, int, bool, TimeSpan, CancellationToken>(async (userId, _, throughLock, _, ct) =>
                throughLock || !((await accounts.Object.GetByIdAsync(userId, ct))?.LockedUntil > DateTime.UtcNow));
    }

    public static AccountCredentials Credentials(
        Mock<IAccountProvider> accounts,
        ISessionProvider sessions,
        IEventBus bus,
        SecurityNotices notices,
        IAccessTokenRevocation accessTokens,
        PasswordHashing? hashing = null)
    {
        ReadLockFromUser(accounts);
        return new AccountCredentials(accounts.Object, sessions, hashing ?? Hashing(), bus, notices, accessTokens, NullLogger<AccountCredentials>.Instance);
    }
}
