using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;
using Wbskt.Infrastructure;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The limits the auth host holds itself: a tenant's workspaces and its members, read from the tenant's
/// plan and handed to the procedure that creates the workspace or joins the member, which counts under a
/// lock on the tenant.
/// </summary>
public sealed class PlanSeatLimitTests
{
    private const int CallerId = 1;
    private const int TenantId = 5;
    private static readonly Guid TenantRef = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly PlanLimits Limits = PlanCatalog.BuiltIn[PlanCode.Maker] with { Workspaces = 3, Members = 4 };

    private readonly Mock<ITenantProvider> _tenants = new();
    private readonly Mock<IWorkspaceProvider> _workspaces = new();

    [Fact]
    public async Task A_tenants_limits_are_its_plans_with_its_overrides()
    {
        var plans = new Mock<ITenantPlanProvider>();
        plans.Setup(p => p.FindByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantPlan(TenantId, TenantRef, PlanCode.Pro, new PlanLimitOverrides { Members = 40 }, 3, null));
        var limits = new TenantLimits(plans.Object, new PlanCatalog(Microsoft.Extensions.Options.Options.Create(new PlanCatalogOptions())));

        PlanLimits resolved = await limits.ForTenantAsync(TenantId, CancellationToken.None);

        Assert.Equal(PlanCatalog.BuiltIn[PlanCode.Pro] with { Members = 40 }, resolved);
    }

    [Fact]
    public async Task A_tenant_that_is_gone_gets_free()
    {
        var limits = new TenantLimits(Mock.Of<ITenantPlanProvider>(), new PlanCatalog(Microsoft.Extensions.Options.Options.Create(new PlanCatalogOptions())));

        Assert.Equal(PlanCatalog.BuiltIn[PlanCode.Free], await limits.ForTenantAsync(TenantRef, CancellationToken.None));
    }

    [Fact]
    public async Task A_seat_refusal_carries_the_plan_its_limit_and_the_seats_held_now()
    {
        var plans = new Mock<ITenantPlanProvider>();
        plans.Setup(p => p.FindByRefIdAsync(TenantRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantPlan(TenantId, TenantRef, PlanCode.Maker, null, 3, null));
        plans.Setup(p => p.GetSeatsAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync((7, 12));
        var limits = new TenantLimits(plans.Object, new PlanCatalog(Microsoft.Extensions.Options.Options.Create(new PlanCatalogOptions())));

        Error workspaces = await limits.WithSeatsAsync(SqlGuardErrors.PlanWorkspaceLimitReached, TenantRef, CancellationToken.None);
        Error members = await limits.WithSeatsAsync(SqlGuardErrors.PlanMemberLimitReached, TenantRef, CancellationToken.None);
        Error other = await limits.WithSeatsAsync(SqlGuardErrors.InvalidInvitation, TenantRef, CancellationToken.None);

        PlanLimits maker = PlanCatalog.BuiltIn[PlanCode.Maker];
        Assert.Equal(new PlanLimitDetail("Maker", maker.Workspaces, 7), workspaces.PlanLimit);
        Assert.Equal(new PlanLimitDetail("Maker", maker.Members, 12), members.PlanLimit);
        Assert.Same(SqlGuardErrors.InvalidInvitation, other);
    }

    [Fact]
    public async Task A_workspace_is_created_under_the_plans_workspace_limit()
    {
        _tenants.Setup(p => p.GetTenantsForUserAsync(CallerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Tenant { Id = TenantId, RefId = TenantRef, Name = "Acme" }]);

        await Workspaces().CreateWorkspaceAsync(CallerId, new CreateWorkspaceRequest("lab", null));

        _workspaces.Verify(p => p.CreateWorkspaceAsync("lab", string.Empty, CallerId, TenantId, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_workspace_past_the_limit_is_a_409_and_announces_nothing()
    {
        var bus = new Mock<IEventBus>();
        _tenants.Setup(p => p.GetTenantsForUserAsync(CallerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Tenant { Id = TenantId, RefId = TenantRef, Name = "Acme" }]);
        _workspaces.Setup(p => p.CreateWorkspaceAsync(It.IsAny<string>(), It.IsAny<string>(), CallerId, TenantId, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50030, "The plan has reached its workspace limit."));

        var result = await Workspaces(bus.Object).CreateWorkspaceAsync(CallerId, new CreateWorkspaceRequest("lab", null));

        Assert.Equal("PLAN_WORKSPACE_LIMIT_REACHED", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invitation_is_issued_under_the_plans_member_limit()
    {
        _tenants.Setup(p => p.CreateInvitationAsync(TenantId, "amal@example.test", null, It.IsAny<byte[]>(), It.IsAny<DateTime>(), CallerId, It.IsAny<IReadOnlyCollection<int>>(), 4, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50031, "The plan has reached its member limit."));

        var result = await Tenants().CreateInvitationAsync(CallerId, TenantId, new CreateInvitationRequest("amal@example.test"));

        Assert.Equal("PLAN_MEMBER_LIMIT_REACHED", result.Error.Code);
    }

    [Fact]
    public async Task Accepting_is_held_to_the_inviting_tenants_member_limit()
    {
        _tenants.Setup(p => p.GetInvitationByTokenHashAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationLookup(Guid.NewGuid(), TenantRef, "Acme", "amal@example.test", DateTime.UtcNow.AddDays(1), IsLive: true));
        _tenants.Setup(p => p.AcceptInvitationAsync(It.IsAny<byte[]>(), CallerId, 4, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50031, "The plan has reached its member limit."));

        var result = await Tenants().AcceptInvitationAsync(CallerId, "invitation-token");

        Assert.Equal("PLAN_MEMBER_LIMIT_REACHED", result.Error.Code);
    }

    [Fact]
    public async Task Signing_up_into_a_full_tenant_keeps_the_account_and_says_why()
    {
        var harness = new AccountServices(limits: new FixedTenantLimits(Limits));
        harness.Tenants.Setup(p => p.GetInvitationByTokenHashAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationLookup(Guid.NewGuid(), TenantRef, "Acme", "someone@example.test", DateTime.UtcNow.AddDays(1), IsLive: true));
        harness.Accounts.Setup(p => p.InsertUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).ReturnsAsync(42);
        harness.Tenants.Setup(p => p.AcceptInvitationAsync(It.IsAny<byte[]>(), 42, 4, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50031, "The plan has reached its member limit."));

        var result = await harness.Registration.RegisterUserAsync("someone", "someone@example.test", "a-long-enough-Passw0rd!", "invitation-token");

        Assert.Equal("PLAN_MEMBER_LIMIT_REACHED", result.Error.Code);
        harness.Tenants.Verify(p => p.CreateTenantAsync("someone's Tenant", null, 42, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private WorkspaceService Workspaces(IEventBus? bus = null) => new(
        _workspaces.Object, Mock.Of<IAccountProvider>(), _tenants.Object, Mock.Of<IAccessControlProvider>(),
        bus ?? Mock.Of<IEventBus>(), NullLogger<WorkspaceService>.Instance, new AuthMetrics(), new FixedTenantLimits(Limits));

    private TenantService Tenants() => new(
        _tenants.Object, Mock.Of<IAccessControlProvider>(), Mock.Of<IAccountProvider>(), _workspaces.Object, Mock.Of<IEventBus>(), Mock.Of<IAuthMailer>(),
        NullLogger<TenantService>.Instance, new FixedTenantLimits(Limits));
}
