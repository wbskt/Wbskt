using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

public interface IBusinessUsageProvider
{
    Task<IReadOnlyCollection<BusinessUsage>> GetByMonthAsync(DateOnly monthStart, CancellationToken ct);
}
