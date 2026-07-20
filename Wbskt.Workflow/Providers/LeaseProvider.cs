using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class LeaseProvider : BaseSqlProvider, ILeaseProvider
{
    public LeaseProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<bool> TryAcquireAsync(string leaseName, string holderId, TimeSpan ttl, CancellationToken ct)
    {
        return await ExecuteScalarAsync<bool>("dbo.Lease_TryAcquire", p =>
        {
            p.AddWithValue("@LeaseName", leaseName);
            p.AddWithValue("@HolderId", holderId);
            p.AddWithValue("@TtlSeconds", (int)ttl.TotalSeconds);
        }, ct);
    }

    public async Task ReleaseAsync(string leaseName, string holderId, CancellationToken ct)
    {
        await ExecuteNonQueryAsync("dbo.Lease_Release", p =>
        {
            p.AddWithValue("@LeaseName", leaseName);
            p.AddWithValue("@HolderId", holderId);
        }, ct);
    }
}
