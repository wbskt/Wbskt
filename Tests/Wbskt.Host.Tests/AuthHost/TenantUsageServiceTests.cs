using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.AuthHost;

public sealed class TenantUsageServiceTests
{
    private readonly Mock<ITenantPlanProvider> _plans = new();
    private readonly PlanCatalog _catalog = new(Microsoft.Extensions.Options.Options.Create(new PlanCatalogOptions()));

    [Fact]
    public async Task Usage_is_the_tenants_plan_with_its_overrides_and_its_seats_against_them()
    {
        _plans.Setup(x => x.FindByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantPlan(4, Guid.NewGuid(), PlanCode.Pro, new PlanLimitOverrides { Members = 40 }, 3, null));
        _plans.Setup(x => x.GetSeatsAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync((2, 7));

        Result<TenantUsageResponse> result = await new TenantUsageService(_plans.Object, _catalog).GetAsync(4, CancellationToken.None);

        PlanLimits pro = PlanCatalog.BuiltIn[PlanCode.Pro] with { Members = 40 };
        Assert.Equal(new TenantUsageResponse(PlanCode.Pro, pro, new UsageCount(2, pro.Workspaces), new UsageCount(7, 40)), result.Value);
    }

    [Fact]
    public async Task A_tenant_gone_since_the_filter_resolved_it_is_not_found()
    {
        Result<TenantUsageResponse> result = await new TenantUsageService(_plans.Object, _catalog).GetAsync(4, CancellationToken.None);

        Assert.Equal("TENANT_NOT_FOUND", result.Error.Code);
    }
}
