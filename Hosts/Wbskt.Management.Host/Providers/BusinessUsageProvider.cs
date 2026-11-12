using System.Data;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class BusinessUsageProvider : BaseSqlProvider, IBusinessUsageProvider
{
    public BusinessUsageProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IReadOnlyCollection<BusinessUsage>> GetByMonthAsync(DateOnly monthStart, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.BusinessUsage_GetBy_Month",
            p => p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue),
            r => new BusinessUsage(
                r.GetGuid(r.GetOrdinal("TenantRefId")),
                PlanLimitOverridesJson.Read(r.IsDBNull(r.GetOrdinal("LimitOverrides")) ? null : r.GetString(r.GetOrdinal("LimitOverrides"))),
                r.GetInt64(r.GetOrdinal("MessagesUsed")),
                r.GetDecimal(r.GetOrdinal("CreditsPosted")),
                r.GetDecimal(r.GetOrdinal("CreditsDrawn"))),
            ct);
    }

    public async Task TrackAsync(DateTime now, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.BusinessTenantMonth_Track",
            p => p.Add("@Now", SqlDbType.DateTime2).Value = now,
            ct);
    }

    public async Task<bool> CloseAsync(DateOnly monthStart, DateTime now, CancellationToken ct)
    {
        return await ExecuteScalarAsync<bool>(
            "dbo.BusinessMonth_Close",
            p =>
            {
                p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
                p.Add("@Now", SqlDbType.DateTime2).Value = now;
            },
            ct);
    }
}
