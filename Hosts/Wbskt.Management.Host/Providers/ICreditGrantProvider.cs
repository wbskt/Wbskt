using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

/// <summary>A tenant's credit grants (<c>dbo.TenantCreditGrants</c>) and the months drawn from them.</summary>
public interface ICreditGrantProvider
{
    /// <summary>Records the grant once per source and reference; false when it was already recorded.</summary>
    Task<bool> InsertAsync(int tenantId, decimal credits, DateTime grantedAtUtc, DateTime expiresAtUtc, string source, string sourceRef, CancellationToken ct);

    /// <summary>Revokes the grant made for the source and reference; false when there was none to revoke.</summary>
    Task<bool> RevokeAsync(string source, string sourceRef, DateTime revokedAtUtc, CancellationToken ct);

    Task<IReadOnlyCollection<UnsettledCreditGrantTenant>> GetUnsettledAsync(DateOnly monthStart, CancellationToken ct);

    /// <summary>Draws the month's spend past <paramref name="allowance"/> from the tenant's grants; null when the month was already settled.</summary>
    Task<decimal?> SettleAsync(int tenantId, DateOnly monthStart, decimal allowance, CancellationToken ct);
}
