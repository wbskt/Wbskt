using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
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
/// messages, so this is the only place that use is counted for a bill. A month is closed
/// <see cref="BillingOptions.BusinessMonthCloseDelay"/> after it ends (by <see cref="TrackAsync"/>, or by the
/// first report asked for it after that), and read as it was closed from then on.
/// </summary>
internal sealed class BusinessOverageService(IBusinessUsageProvider usage, PlanCatalog catalog, TimeProvider clock, IOptions<BillingOptions> options) : IBusinessOverageService
{
    private const int MonthBusyError = 50035; // THROW in dbo.BusinessMonth_Close

    private readonly TimeSpan _closeDelay = options.Value.BusinessMonthCloseDelay;

    public async Task<Result<BusinessOverageReport>> GetAsync(int year, int month, CancellationToken ct)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (month is < 1 or > 12 || year < 2000 || new DateOnly(year, month, 1) > new DateOnly(now.Year, now.Month, 1))
        {
            return Result<BusinessOverageReport>.Failure(Error.Validation("BILLING_MONTH_INVALID", "'year' and 'month' must name this month or an earlier one."));
        }

        var monthStart = new DateOnly(year, month, 1);
        bool closed = false;
        if (IsDue(monthStart, now))
        {
            try
            {
                closed = await usage.CloseAsync(monthStart, now, ct);
            }
            catch (SqlException ex) when (ex.Number == MonthBusyError)
            {
                return Result<BusinessOverageReport>.Failure(Error.Unavailable("BILLING_MONTH_BUSY", "The month is being closed; try again."));
            }
        }

        IReadOnlyCollection<BusinessUsage> tenants = await usage.GetByMonthAsync(monthStart, ct);
        return Result<BusinessOverageReport>.Success(new BusinessOverageReport(monthStart, [.. tenants.Select(Overage)], closed));
    }

    public async Task TrackAsync(CancellationToken ct)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        await usage.TrackAsync(now, ct);

        var lastMonth = new DateOnly(now.Year, now.Month, 1).AddMonths(-1);
        if (IsDue(lastMonth, now))
        {
            await usage.CloseAsync(lastMonth, now, ct);
        }
    }

    private bool IsDue(DateOnly monthStart, DateTime now) =>
        now >= monthStart.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) + _closeDelay;

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
