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
            .ReturnsAsync(new WorkspaceUsageSummary(1500m, 400m, 900_000, 250_000, 60, 12, 5, 1, 1000m, 500m));
        _usage.Setup(x => x.GetDailyCreditsAsync(7, new DateOnly(2026, 10, 9), new DateOnly(2026, 11, 9), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DailyCreditUsage(new DateOnly(2026, 11, 2), 400m)]);
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(PlanCode.Maker, tenantId: 3), new FixedClock());

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, null, null, CancellationToken.None);

        PlanLimits maker = PlanCatalog.BuiltIn[PlanCode.Maker];
        WorkspaceUsageResponse usage = result.Value;
        Assert.Equal(PlanCode.Maker, usage.Plan);
        Assert.Equal((new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)), (usage.PeriodStart, usage.PeriodEnd));
        Assert.Equal(new CreditUsageAmount(1500m, maker.CreditsPerMonth, 500m), usage.Account.Credits);
        Assert.Equal(new MessageUsageAmount(900_000, maker.MessagesPerMonth, false), usage.Account.Messages);
        Assert.Equal(new UsageCount(60, maker.Clients), usage.Account.Clients);
        Assert.Equal(new UsageCount(5, maker.ActiveRuns), usage.Account.ActiveRuns);
        Assert.Equal(new WorkspaceUsageShare(400m, 1000m, 250_000, 12, 1), usage.Workspace);
        Assert.Single(usage.Days);
    }

    [Fact]
    public async Task A_range_ending_at_midnight_leaves_out_the_day_it_starts()
    {
        _usage.Setup(x => x.GetSummaryAsync(7, null, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceUsageSummary(0, 0, 0, 0, 0, 0, 0, 0, null, 0));
        _usage.Setup(x => x.GetDailyCreditsAsync(7, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(), new FixedClock());

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _usage.Verify(x => x.GetDailyCreditsAsync(7, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData(PlanCode.Free, 1_500_000, true)]
    [InlineData(PlanCode.Free, 1_499_999, false)]
    [InlineData(PlanCode.Maker, 20_000_000, false)]
    public async Task Only_a_free_account_that_used_its_messages_is_throttled(PlanCode plan, long used, bool throttled)
    {
        _usage.Setup(x => x.GetSummaryAsync(7, null, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceUsageSummary(0, 0, used, used, 0, 0, 0, 0, null, 0));
        _usage.Setup(x => x.GetDailyCreditsAsync(7, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(plan), new FixedClock());

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, null, null, CancellationToken.None);

        Assert.Equal(throttled, result.Value.Account.Messages.Throttled);
    }

    [Fact]
    public async Task A_range_longer_than_a_year_is_refused()
    {
        var service = new WorkspaceUsageService(_usage.Object, new FixedWorkspacePlans(), new FixedClock());

        Result<WorkspaceUsageResponse> result = await service.GetAsync(7, Now.AddDays(-400), Now, CancellationToken.None);

        Assert.Equal("TIME_RANGE_TOO_LONG", result.Error.Code);
        _usage.VerifyNoOtherCalls();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
