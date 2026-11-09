using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// What each Business tenant owes past its contract for a month: its messages past the contract's monthly
/// messages, and the credits its plan covered past its monthly credits. Business runs carry on to
/// <see cref="BusinessContract.CreditOverageMultiple"/> times their credits and are never throttled on
/// messages, so this is the only place that use is counted for a bill.
/// </summary>
internal sealed class BusinessOverageService(IBusinessUsageProvider usage, PlanCatalog catalog, TimeProvider clock) : IBusinessOverageService
{
    public async Task<Result<BusinessOverageReport>> GetAsync(int year, int month, CancellationToken ct)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (month is < 1 or > 12 || year < 2000 || new DateOnly(year, month, 1) > new DateOnly(now.Year, now.Month, 1))
        {
            return Result<BusinessOverageReport>.Failure(Error.Validation("BILLING_MONTH_INVALID", "'year' and 'month' must name this month or an earlier one."));
        }

        var monthStart = new DateOnly(year, month, 1);
        IReadOnlyCollection<BusinessUsage> tenants = await usage.GetByMonthAsync(monthStart, ct);
        return Result<BusinessOverageReport>.Success(new BusinessOverageReport(monthStart, [.. tenants.Select(Overage)]));
    }

    private BusinessOverage Overage(BusinessUsage used)
    {
        PlanLimits limits = catalog.LimitsFor(PlanCode.Business, used.LimitOverrides);
        long extraMessages = Math.Max(0, used.MessagesUsed - limits.MessagesPerMonth);
        decimal extraCredits = Math.Max(0m, used.CreditsPosted - used.CreditsDrawn - limits.CreditsPerMonth);
        decimal amount = extraMessages / 1_000_000m * BusinessContract.UsdPerMillionExtraMessages
            + extraCredits / 1_000m * BusinessContract.UsdPerThousandExtraCredits;

        return new BusinessOverage(
            used.TenantRefId,
            limits.MessagesPerMonth,
            used.MessagesUsed,
            extraMessages,
            limits.CreditsPerMonth,
            used.CreditsPosted,
            used.CreditsDrawn,
            extraCredits,
            Math.Round(amount, 2, MidpointRounding.AwayFromZero));
    }
}
