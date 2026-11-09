using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class CreditGrantSettlementTests
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private readonly Mock<ICreditGrantProvider> _grants = new();

    [Fact]
    public async Task Last_month_is_settled_against_each_tenants_plan_and_its_overrides()
    {
        _grants.Setup(x => x.GetUnsettledAsync(October, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new UnsettledCreditGrantTenant(1, PlanCode.Maker, null),
            new UnsettledCreditGrantTenant(2, PlanCode.Free, new PlanLimitOverrides { CreditsPerMonth = 7m })
        ]);

        await Settlement(new DateTimeOffset(2026, 11, 9, 4, 0, 0, TimeSpan.Zero)).SettleLastMonthAsync(CancellationToken.None);

        _grants.Verify(x => x.SettleAsync(1, October, PlanCatalog.BuiltIn[PlanCode.Maker].CreditsPerMonth, It.IsAny<CancellationToken>()));
        _grants.Verify(x => x.SettleAsync(2, October, 7m, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task The_first_hour_of_a_month_waits_for_the_last_credits_of_the_one_before()
    {
        await Settlement(new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.Zero)).SettleLastMonthAsync(CancellationToken.None);

        _grants.VerifyNoOtherCalls();
    }

    private CreditGrantSettlement Settlement(DateTimeOffset now) =>
        new(_grants.Object, new PlanCatalog(Options.Create(new PlanCatalogOptions())), new FixedClock(now), NullLogger<CreditGrantSettlement>.Instance);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
