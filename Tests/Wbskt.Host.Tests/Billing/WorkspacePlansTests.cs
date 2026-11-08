using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.Billing;

public sealed class WorkspacePlansTests
{
    private readonly Mock<IWorkspaceAccountProvider> _accounts = new();
    private readonly PlanCatalog _catalog = new(Options.Create(new PlanCatalogOptions()));

    [Fact]
    public async Task A_workspace_with_no_copy_yet_is_on_free()
    {
        WorkspacePlan plan = await Plans().GetAsync(7, CancellationToken.None);

        Assert.Equal(PlanCode.Free, plan.PlanCode);
        Assert.Null(plan.TenantId);
        Assert.Equal(PlanCatalog.BuiltIn[PlanCode.Free], plan.Limits);
    }

    [Fact]
    public async Task A_copied_account_gives_its_plan_with_the_tenant_overrides()
    {
        _accounts.Setup(a => a.FindAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceAccount(7, 3, Guid.NewGuid(), PlanCode.Business, new PlanLimitOverrides { Clients = 5_000 }, 2));

        WorkspacePlan plan = await Plans().GetAsync(7, CancellationToken.None);

        Assert.Equal(PlanCode.Business, plan.PlanCode);
        Assert.Equal(3, plan.TenantId);
        Assert.Equal(5_000, plan.Limits.Clients);
    }

    [Fact]
    public async Task A_plan_is_read_once_until_it_is_invalidated()
    {
        _accounts.Setup(a => a.FindAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceAccount(7, 3, Guid.NewGuid(), PlanCode.Maker, null, 1));
        IWorkspacePlans plans = Plans();

        await plans.GetAsync(7, CancellationToken.None);
        await plans.GetAsync(7, CancellationToken.None);
        _accounts.Verify(a => a.FindAsync(7, It.IsAny<CancellationToken>()), Times.Once);

        plans.Invalidate(7);
        await plans.GetAsync(7, CancellationToken.None);
        _accounts.Verify(a => a.FindAsync(7, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private IWorkspacePlans Plans() => new WorkspacePlans(_accounts.Object, _catalog, new MemoryCache(new MemoryCacheOptions()));
}
