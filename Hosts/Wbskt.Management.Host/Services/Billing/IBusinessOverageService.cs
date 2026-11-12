using Wbskt.Infrastructure;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Billing;

public interface IBusinessOverageService
{
    Task<Result<BusinessOverageReport>> GetAsync(int year, int month, CancellationToken ct);

    /// <summary>Records the tenants on Business now against this month, and closes last month once it is due.</summary>
    Task TrackAsync(CancellationToken ct);
}
