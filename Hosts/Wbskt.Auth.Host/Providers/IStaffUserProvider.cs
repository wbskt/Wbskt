namespace Wbskt.Auth.Host.Providers;

/// <summary>What staff do through the admin host that no console route does: writes to a person's account, and finding any tenant.</summary>
internal interface IStaffUserProvider
{
    /// <summary>Lifts the account's sign-in lockout and clears its failure count.</summary>
    Task ClearLockoutAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Turns sign-in on or off; off also revokes every live refresh token, in one transaction.</summary>
    Task SetActiveAsync(int userId, bool isActive, string revokedByIp, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a never-verified account to <paramref name="email"/>, unverified, spends every outstanding
    /// verification link and pending address change, and issues a new verification token, in one
    /// transaction. Throws <c>SqlException</c> 50046 when the address is verified by now, 50047 when
    /// another account holds the new one.
    /// </summary>
    Task CorrectUnverifiedEmailAsync(int userId, string email, byte[] tokenHash, DateTime expiresAt, CancellationToken cancellationToken);

    /// <summary>
    /// Makes a member an administrator of the tenant: its Admin role tenant-wide, without their own
    /// tenant-wide denies of users.manage and roles.manage. Returns the Admin role's RefId. Throws
    /// <c>SqlException</c> 50016 when the person is not a member, 50048 when they are suspended there or
    /// their sign-in is off.
    /// </summary>
    Task<Guid> RepairAdministratorAsync(int tenantId, int userId, CancellationToken cancellationToken);

    /// <summary>
    /// The tenant's id, or 0. Not scoped to a member, unlike <see cref="ITenantProvider"/>'s lookups,
    /// because staff act on every tenant; only the admin key reaches the routes that use it.
    /// </summary>
    Task<int> FindTenantIdByRefIdAsync(Guid tenantRef, CancellationToken cancellationToken);
}
