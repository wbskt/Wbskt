using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class BusinessOverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeUsage _usage = new();

    [Fact]
    public async Task A_contract_is_billed_for_messages_and_plan_credits_past_it_at_the_contract_rates()
    {
        var contract = new PlanLimitOverrides { MessagesPerMonth = 300_000_000, CreditsPerMonth = 900_000m };
        _usage.Rows = [new BusinessUsage(Guid.NewGuid(), contract, MessagesUsed: 312_500_000, CreditsPosted: 1_000_000m, CreditsDrawn: 40_000m)];

        Result<BusinessOverageReport> report = await Service().GetAsync(2026, 10, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 1), _usage.MonthAsked);
        BusinessOverage bill = Assert.Single(report.Value.Tenants);
        Assert.Equal((300_000_000L, 12_500_000L), (bill.MessagesPerMonth, bill.ExtraMessages));
        Assert.Equal((900_000m, 60_000m), (bill.CreditsPerMonth, bill.ExtraCredits));

        // 12.5 million messages at $1 a million, and 60,000 credits at $0.50 a thousand.
        Assert.Equal(12.50m + 30m, bill.AmountUsd);
    }

    [Fact]
    public async Task A_month_inside_its_contract_owes_nothing()
    {
        _usage.Rows = [new BusinessUsage(Guid.NewGuid(), null, MessagesUsed: 10, CreditsPosted: 700_000m, CreditsDrawn: 200_000m)];

        BusinessOverage bill = Assert.Single((await Service().GetAsync(2026, 11, CancellationToken.None)).Value.Tenants);

        Assert.Equal((0L, 0m, 0m), (bill.ExtraMessages, bill.ExtraCredits, bill.AmountUsd));
    }

    [Theory]
    [InlineData(2026, 12)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1999, 1)]
    public async Task A_month_that_has_not_started_or_does_not_exist_is_refused(int year, int month)
    {
        Result<BusinessOverageReport> report = await Service().GetAsync(year, month, CancellationToken.None);

        Assert.Equal("BILLING_MONTH_INVALID", report.Error.Code);
        Assert.Equal(ErrorType.Validation, report.Error.Type);
        Assert.Null(_usage.MonthAsked);
    }

    [Fact]
    public void The_report_takes_the_internal_key_and_no_user_token()
    {
        Type controller = typeof(InternalBillingController);

        Assert.NotNull(controller.GetCustomAttribute<InternalApiKeyAttribute>());
        Assert.NotNull(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.StartsWith(InternalRoutes.Prefix + "/", InternalRoutes.BusinessOverage);
    }

    private BusinessOverageService Service() =>
        new(_usage, new PlanCatalog(Options.Create(new PlanCatalogOptions())), new FixedClock(Now));

    private sealed class FakeUsage : IBusinessUsageProvider
    {
        public IReadOnlyCollection<BusinessUsage> Rows { get; set; } = [];

        public DateOnly? MonthAsked { get; private set; }

        public Task<IReadOnlyCollection<BusinessUsage>> GetByMonthAsync(DateOnly monthStart, CancellationToken ct)
        {
            MonthAsked = monthStart;
            return Task.FromResult(Rows);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
