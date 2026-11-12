using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class WorkspaceUsageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 4, 0, 0, TimeSpan.Zero);
    private readonly Mock<IWorkspaceUsageProvider> _usage = new();

    [Fact]
    public async Task Usage_is_the_plans_limits_against_the_accounts_totals_and_the_workspaces_share()
    {
        _usage.Setup(x => x.GetSummaryAsync(7, 3, new DateOnly(2026, 11, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceUsageSummary(1500m, 400m, 900_000, 250_000, 60, 12, 5, 1, 1000m, 500m, 120m, new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc), 200m, TenantCreditsPosted: 1400m));
        _usage.Setup(x => x.GetDailyCreditsAsync(7, new DateOnly(2026, 10, 9), new DateOnly(2026, 11, 9), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DailyCreditUsage(new DateOnly(2026, 11, 2), 400m)]);
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(PlanCode.Maker, tenantId: 3), new FixedClock(), Options.Create(new BillingOptions()));

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, null, null, CancellationToken.None);

        PlanLimits maker = PlanCatalog.BuiltIn[PlanCode.Maker];
        WorkspaceUsageResponse usage = result.Value;
        Assert.Equal(PlanCode.Maker, usage.Plan);
        Assert.Equal((new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)), (usage.PeriodStart, usage.PeriodEnd));
        Assert.Equal(new CreditUsageAmount(1500m, maker.CreditsPerMonth, 500m, new DateTimeOffset(2027, 1, 15, 0, 0, 0, TimeSpan.Zero), 200m, maker.CreditsPerMonth - 1400m + 500m - 100m), usage.Account.Credits);
        Assert.Equal(new MessageUsageAmount(900_000, maker.MessagesPerMonth, false), usage.Account.Messages);
        Assert.Equal(new UsageCount(60, maker.Clients), usage.Account.Clients);
        Assert.Equal(new UsageCount(5, maker.ActiveRuns), usage.Account.ActiveRuns);
        Assert.Equal(new WorkspaceUsageShare(400m, 1000m, 250_000, 12, 1, 120m), usage.Workspace);
        Assert.Single(usage.Days);
    }

    // Maker's 25,000 with 30,000 posted, 5,000 of it drawn from a 10,000 pack that now holds 5,000: the
    // plan is used up and the pack's 5,000 are left, as Run_Start counts them.
    [Theory]
    [InlineData(30_000, 30_000, 5_000, 5_000, 5_000)]

    // The same with 1,000 a run has not posted yet: it comes out of what is left.
    [InlineData(31_000, 30_000, 5_000, 5_000, 4_000)]

    // Nothing left is 0, never below.
    [InlineData(40_000, 35_000, 5_000, 0, 0)]

    // Past the plan with nothing drawn (a downgrade) and a pack still live: the plan adds nothing, the pack is left.
    [InlineData(30_000, 30_000, 0, 5_000, 5_000)]

    // The first case with 1,500 held by the budgets of runs going: those come out of what is left too.
    [InlineData(30_000, 30_000, 5_000, 5_000, 3_500, 1_500)]
    public async Task Remaining_is_what_a_run_start_would_find_left(double used, double posted, double drawn, double topUp, double remaining, double reserved = 0)
    {
        _usage.Setup(x => x.GetSummaryAsync(7, null, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceUsageSummary((decimal)used, 0, 0, 0, 0, 0, 0, 0, null, (decimal)topUp, TenantCreditsPosted: (decimal)posted, TenantCreditsDrawn: (decimal)drawn, TenantCreditsReserved: (decimal)reserved));
        _usage.Setup(x => x.GetDailyCreditsAsync(7, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(PlanCode.Maker), new FixedClock(), Options.Create(new BillingOptions()));

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, null, null, CancellationToken.None);

        Assert.Equal((decimal)remaining, result.Value.Account.Credits.Remaining);
    }

    [Fact]
    public async Task Business_has_its_overage_left_past_the_allowance()
    {
        _usage.Setup(x => x.GetSummaryAsync(7, null, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceUsageSummary(0, 0, 0, 0, 0, 0, 0, 0, null, 0));
        _usage.Setup(x => x.GetDailyCreditsAsync(7, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(PlanCode.Business), new FixedClock(), Options.Create(new BillingOptions()));

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, null, null, CancellationToken.None);

        Assert.Equal(PlanCatalog.BuiltIn[PlanCode.Business].CreditsPerMonth * BusinessContract.CreditOverageMultiple, result.Value.Account.Credits.Remaining);
    }

    [Fact]
    public async Task A_range_ending_at_midnight_leaves_out_the_day_it_starts()
    {
        _usage.Setup(x => x.GetSummaryAsync(7, null, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceUsageSummary(0, 0, 0, 0, 0, 0, 0, 0, null, 0));
        _usage.Setup(x => x.GetDailyCreditsAsync(7, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(), new FixedClock(), Options.Create(new BillingOptions()));

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _usage.Verify(x => x.GetDailyCreditsAsync(7, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData(PlanCode.Free, 1_500_000, true)]
    [InlineData(PlanCode.Free, 1_499_999, false)]
    [InlineData(PlanCode.Maker, 19_999_999, false)]
    [InlineData(PlanCode.Maker, 20_000_000, true)]
    [InlineData(PlanCode.Business, 1_000_000_000, false)]
    public async Task Free_is_throttled_at_its_messages_a_paid_plan_at_twice_them_and_business_never(PlanCode plan, long used, bool throttled)
    {
        _usage.Setup(x => x.GetSummaryAsync(7, null, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceUsageSummary(0, 0, used, used, 0, 0, 0, 0, null, 0));
        _usage.Setup(x => x.GetDailyCreditsAsync(7, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(plan), new FixedClock(), Options.Create(new BillingOptions()));

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, null, null, CancellationToken.None);

        Assert.Equal(throttled, result.Value.Account.Messages.Throttled);
    }

    [Fact]
    public async Task A_range_longer_than_a_year_is_refused()
    {
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(), new FixedClock(), Options.Create(new BillingOptions()));

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, Now.AddDays(-400), Now, CancellationToken.None);

        Assert.Equal("TIME_RANGE_TOO_LONG", result.Error.Code);
        _usage.VerifyNoOtherCalls();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
