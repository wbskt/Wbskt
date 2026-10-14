using Wbskt.Auth.Host.Models;
using Wbskt.Primitives;

namespace Wbskt.Auth.Host.Providers;

/// <summary>
/// Accounts: the user record and its credentials, the count of wrong passwords against it, and the
/// single-use tokens that reset a password or confirm an address. <c>FindIdByRefIdAsync</c> finds a
/// user by reference regardless of tenant.
/// </summary>
internal interface IAccountProvider : IReferenceProvider
{
    Task<User> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<int> InsertUserAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts a wrong password (<c>User_RecordLoginFailure</c>) and returns the account's lock expiry,
    /// which is set when this failure reached <paramref name="maxFailures"/>.
    /// </summary>
    Task<DateTime?> RecordLoginFailureAsync(int userId, int maxFailures, TimeSpan lockout, CancellationToken cancellationToken = default);

    /// <summary>Clears the failure count and stamps the last sign-in.</summary>
    Task RecordLoginSuccessAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a stronger hash of the same password (<c>User_UpgradePasswordHash</c>), only while the
    /// stored hash is still <paramref name="currentPasswordHash"/>. Returns false when it had changed.
    /// </summary>
    Task<bool> UpgradePasswordHashAsync(int userId, string currentPasswordHash, string newPasswordHash, CancellationToken cancellationToken = default);

    /// <summary>Writes the new password and revokes every refresh token the user holds, in one transaction.</summary>
    Task ChangePasswordAsync(int userId, string passwordHash, string? revokedByIp, CancellationToken cancellationToken = default);

    // Account recovery and address verification. As with invitations, the raw token never reaches
    // this layer -- the service hashes it, so a provider that logged its parameters could not leak a
    // usable one.
    Task CreatePasswordResetTokenAsync(int userId, byte[] tokenHash, DateTime expiresAt, string? requestedByIp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the new password and revokes every refresh token the account has, in one transaction,
    /// and returns the user id. Throws <see cref="Wbskt.Primitives.Exceptions.SecurityException"/>
    /// when the token is unknown, spent or expired -- one answer for all three.
    /// </summary>
    Task<int> ConsumePasswordResetTokenAsync(byte[] tokenHash, string passwordHash, string? revokedByIp, CancellationToken cancellationToken = default);

    Task CreateEmailVerificationTokenAsync(int userId, byte[] tokenHash, DateTime expiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the account's address verified and returns the user id. Throws
    /// <see cref="Wbskt.Primitives.Exceptions.SecurityException"/> when the token is not valid.
    /// </summary>
    Task<int> ConsumeEmailVerificationTokenAsync(byte[] tokenHash, CancellationToken cancellationToken = default);
}
