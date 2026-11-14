namespace Wbskt.Auth.Host.Providers;

/// <summary>What staff do through the admin host that no console route does: writes to a person's account, and finding any tenant.</summary>
internal interface IStaffUserProvider
{
    /// <summary>Lifts the account's sign-in lockout and clears its failure count.</summary>
    Task ClearLockoutAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Turns sign-in on or off; off also revokes every live refresh token, in one transaction.</summary>
    Task SetActiveAsync(int userId, bool isActive, string revokedByIp, CancellationToken cancellationToken);

    /// <summary>
    /// The tenant's id, or 0. Not scoped to a member, unlike <see cref="ITenantProvider"/>'s lookups,
    /// because staff act on every tenant; only the admin key reaches the routes that use it.
    /// </summary>
    Task<int> FindTenantIdByRefIdAsync(Guid tenantRef, CancellationToken cancellationToken);
}
