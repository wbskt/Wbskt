namespace Wbskt.Auth.Host.Providers;

/// <summary>The writes staff make to a person's account that no console route makes.</summary>
internal interface IStaffUserProvider
{
    /// <summary>Lifts the account's sign-in lockout and clears its failure count.</summary>
    Task ClearLockoutAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Turns sign-in on or off; off also revokes every live refresh token, in one transaction.</summary>
    Task SetActiveAsync(int userId, bool isActive, string revokedByIp, CancellationToken cancellationToken);
}
