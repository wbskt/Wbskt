using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

/// <summary>A tenant's credit grants (<c>dbo.TenantCreditGrants</c>): recorded, revoked and held as its payments change.</summary>
public interface ICreditGrantProvider
{
    /// <summary>Records the grant once per source and reference; false when it was already recorded.</summary>
    Task<bool> InsertAsync(int tenantId, decimal credits, DateTime grantedAtUtc, DateTime expiresAtUtc, string source, string sourceRef, CancellationToken ct);

    /// <summary>Revokes the grant made for the source and reference; false when there was none to revoke.</summary>
    Task<bool> RevokeAsync(string source, string sourceRef, DateTime revokedAtUtc, CancellationToken ct);

    /// <summary>
    /// Holds the grant while a chargeback is open, or releases it, when <paramref name="revision"/> is newer
    /// than the one it holds; false when there was no such grant or the revision was not newer.
    /// </summary>
    Task<bool> SetHoldAsync(string source, string sourceRef, bool held, int revision, DateTime atUtc, CancellationToken ct);

    /// <summary>
    /// Claims the live grants with credits left that expire after <paramref name="fromUtc"/> and by
    /// <paramref name="toUtc"/> and whose tenant was not told yet; each is returned once.
    /// </summary>
    Task<IReadOnlyCollection<ExpiringCreditGrant>> ClaimExpiringAsync(DateTime fromUtc, DateTime toUtc, DateTime atUtc, CancellationToken ct);

    /// <summary>Gives back a claimed expiry warning, so it is claimed again next time.</summary>
    Task ReleaseExpiryWarningAsync(string source, string sourceRef, CancellationToken ct);
}
