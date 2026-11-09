using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// Closes last month for every tenant with credit grants: what the month spent past the plan's monthly
/// credits is drawn from the grants, soonest to expire first (<c>dbo.TenantCreditGrants_Settle</c>). Waits
/// <see cref="PostingGrace"/> into the new month so the engine has posted the old month's last credits.
/// Each tenant's month is settled once, however many replicas try.
/// </summary>
public sealed class CreditGrantSettlement(ICreditGrantProvider grants, PlanCatalog catalog, TimeProvider clock, ILogger<CreditGrantSettlement> logger)
{
    internal static readonly TimeSpan PostingGrace = TimeSpan.FromHours(1);

    public async Task SettleLastMonthAsync(CancellationToken ct)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        var thisMonth = new DateOnly(now.Year, now.Month, 1);
        if (now - thisMonth.ToDateTime(TimeOnly.MinValue) < PostingGrace)
        {
            return;
        }

        DateOnly lastMonth = thisMonth.AddMonths(-1);
        foreach (UnsettledCreditGrantTenant tenant in await grants.GetUnsettledAsync(lastMonth, ct))
        {
            decimal allowance = catalog.LimitsFor(tenant.PlanCode, tenant.LimitOverrides).CreditsPerMonth;
            decimal? drawn = await grants.SettleAsync(tenant.TenantId, lastMonth, allowance, ct);
            if (drawn > 0)
            {
                logger.LogInformation("Drew {Drawn} credits from tenant {TenantId}'s grants for {Month:yyyy-MM}.", drawn, tenant.TenantId, lastMonth);
            }
        }
    }
}
