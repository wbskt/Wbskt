using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

public interface IBusinessUsageProvider
{
    Task<IReadOnlyCollection<BusinessUsage>> GetByMonthAsync(DateOnly monthStart, CancellationToken ct);

    /// <summary>Records the tenants on Business now against the month holding <paramref name="now"/>.</summary>
    Task TrackAsync(DateTime now, CancellationToken ct);

    /// <summary>Keeps the month's Business tenants and totals as they are now; closing a closed month does nothing.</summary>
    Task CloseAsync(DateOnly monthStart, DateTime now, CancellationToken ct);
}
