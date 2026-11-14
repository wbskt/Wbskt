using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Controllers;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Events.Billing;
using Wbskt.Host.Tests.Infrastructure;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Infrastructure.Billing;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The platform admin host's routes on the auth host: closed (404) until the admin key is configured,
/// opened by that key alone, and left out of the OpenAPI document.
/// </summary>
public sealed class AdminTenantsTests
{
    private const string AdminKey = "admin-key";
    private const string SharedKey = "shared-key";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly StaffChange Staff = new(Guid.NewGuid(), "pika", "Raising the cap for a pilot.", Guid.NewGuid());

    [Fact]
    public void Without_a_configured_key_the_routes_answer_as_if_they_did_not_exist()
    {
        var context = FilterContext(adminHeader: AdminKey, configured: null);

        new AdminApiKeyAttribute().OnAuthorization(context);

        Assert.IsType<NotFoundResult>(context.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public void A_call_without_the_admin_key_is_refused(string? sent)
    {
        var context = FilterContext(adminHeader: sent, configured: AdminKey);

        new AdminApiKeyAttribute().OnAuthorization(context);

        Assert.Equal("ADMIN_API_KEY_INVALID", ProblemAssert.Is(context.Result, StatusCodes.Status401Unauthorized));
    }

    [Fact]
    public void The_shared_internal_key_does_not_open_the_admin_routes()
    {
        var context = FilterContext(adminHeader: null, configured: AdminKey, internalHeader: SharedKey);

        new AdminApiKeyAttribute().OnAuthorization(context);

        Assert.Equal("ADMIN_API_KEY_INVALID", ProblemAssert.Is(context.Result, StatusCodes.Status401Unauthorized));
    }

    [Fact]
    public void The_admin_key_is_let_through()
    {
        var context = FilterContext(adminHeader: AdminKey, configured: AdminKey);

        new AdminApiKeyAttribute().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Every_admin_action_takes_the_admin_key_under_the_admin_prefix_and_stays_out_of_the_api_document()
    {
        var controller = typeof(AdminTenantsController);
        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(controller.GetCustomAttribute<AdminApiKeyAttribute>());
        Assert.Null(controller.GetCustomAttribute<InternalApiKeyAttribute>());
        Assert.True(controller.GetCustomAttribute<ApiExplorerSettingsAttribute>()!.IgnoreApi);
        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.StartsWith(InternalRoutes.AdminPrefix + "/", a.GetCustomAttribute<HttpMethodAttribute>()!.Template));
    }

    [Fact]
    public async Task The_limits_are_the_plan_as_stored_and_the_limits_it_runs_under()
    {
        var tenantRef = Guid.NewGuid();
        var plan = new TenantPlanResponse(tenantRef, PlanCode.Pro, new PlanLimitOverrides { Clients = 500 }, 7, DateTimeOffset.UnixEpoch);
        var plans = new Mock<ITenantPlanService>();
        plans.Setup(p => p.GetAsync(tenantRef, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var limits = new FixedTenantLimits(PlanCatalog.BuiltIn[PlanCode.Pro] with { Clients = 500 });

        var result = await Service(plans.Object, limits).GetLimitsAsync(tenantRef, CancellationToken.None);

        Assert.Equal(new AdminTenantLimitsResponse(plan, limits.Limits), result.Value);
    }

    [Fact]
    public async Task An_unknown_tenant_is_the_plan_services_not_found()
    {
        var plans = new Mock<ITenantPlanService>();
        plans.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.NotFound("TENANT_NOT_FOUND", "No tenant."));

        var result = await Service(plans.Object, new FixedTenantLimits()).GetLimitsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("TENANT_NOT_FOUND", result.Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(367)]
    public async Task A_staff_plan_ends_in_the_future_and_within_a_year(int days)
    {
        var runner = new RecordingRunner();
        var plans = new Mock<ITenantPlanService>(MockBehavior.Strict);

        var result = await Service(plans.Object, runner: runner).SetPlanAsync(
            Guid.NewGuid(), new AdminSetTenantPlanRequest(Staff, PlanCode.Pro, null, 3, Now.AddDays(days)), CancellationToken.None);

        Assert.Equal("PLAN_ENDS_AT_INVALID", result.Error.Code);
        Assert.Null(runner.Action);
    }

    [Fact]
    public async Task A_staff_plan_is_set_as_the_operators_through_the_runner()
    {
        var tenantRef = Guid.NewGuid();
        var endsAt = Now.AddDays(30);
        var runner = new RecordingRunner();
        var plans = new Mock<ITenantPlanService>();
        plans.Setup(p => p.SetAsync(tenantRef, It.IsAny<SetTenantPlanRequest>(), PlanSource.Operator, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantPlanResponse(tenantRef, PlanCode.Business, null, 4, Now, PlanSource.Operator, endsAt));

        var result = await Service(plans.Object, runner: runner).SetPlanAsync(
            tenantRef, new AdminSetTenantPlanRequest(Staff, PlanCode.Business, null, 3, endsAt, 250), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new StaffAction(AdminTenantService.SetPlanAction, AdminTenantService.TargetKind, tenantRef), runner.Action);
        Assert.Equal(Staff, runner.Staff);
        plans.Verify(p => p.SetAsync(tenantRef, new SetTenantPlanRequest(PlanCode.Business, null, 3, endsAt, 250), PlanSource.Operator, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_refused_plan_change_is_the_runners_refusal()
    {
        var plans = new Mock<ITenantPlanService>();
        plans.Setup(p => p.SetAsync(It.IsAny<Guid>(), It.IsAny<SetTenantPlanRequest>(), It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Conflict("TENANT_PLAN_CHANGED", "Changed."));

        var result = await Service(plans.Object, runner: new RecordingRunner()).SetPlanAsync(
            Guid.NewGuid(), new AdminSetTenantPlanRequest(Staff, PlanCode.Pro, null, 3, Now.AddDays(30)), CancellationToken.None);

        Assert.Equal("TENANT_PLAN_CHANGED", result.Error.Code);
    }

    [Fact]
    public async Task A_limits_change_keeps_the_plan_its_source_and_its_end()
    {
        var tenantRef = Guid.NewGuid();
        var runner = new RecordingRunner();
        var current = new TenantPlanResponse(tenantRef, PlanCode.Business, new PlanLimitOverrides { Clients = 300 }, 9, Now, PlanSource.Subscription);
        var plans = new Mock<ITenantPlanService>();
        plans.Setup(p => p.GetAsync(tenantRef, It.IsAny<CancellationToken>())).ReturnsAsync(current);
        plans.Setup(p => p.SetAsync(tenantRef, It.IsAny<SetTenantPlanRequest>(), PlanSource.Subscription, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current with { PlanRevision = 10 });
        var overrides = new PlanLimitOverrides { Clients = 900 };

        var result = await Service(plans.Object, runner: runner).SetLimitsAsync(
            tenantRef, new AdminSetTenantLimitsRequest(Staff, overrides, 9), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new StaffAction(AdminTenantService.SetLimitsAction, AdminTenantService.TargetKind, tenantRef), runner.Action);
        plans.Verify(p => p.SetAsync(tenantRef, new SetTenantPlanRequest(PlanCode.Business, overrides, 9, null, null), PlanSource.Subscription, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_limits_change_for_an_unknown_tenant_is_not_found_and_not_run()
    {
        var runner = new RecordingRunner();
        var plans = new Mock<ITenantPlanService>();
        plans.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.NotFound("TENANT_NOT_FOUND", "No tenant."));

        var result = await Service(plans.Object, runner: runner).SetLimitsAsync(
            Guid.NewGuid(), new AdminSetTenantLimitsRequest(Staff, null, 1), CancellationToken.None);

        Assert.Equal("TENANT_NOT_FOUND", result.Error.Code);
        Assert.Null(runner.Action);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(-5, 30)]
    [InlineData(100_001, 30)]
    [InlineData(0.00001, 30)]
    [InlineData(500, 0)]
    [InlineData(500, 367)]
    public async Task Staff_credits_are_1_to_100000_and_expire_within_a_year(double credits, int days)
    {
        var runner = new RecordingRunner();

        var result = await Service(new Mock<ITenantPlanService>(MockBehavior.Strict).Object, runner: runner).GrantCreditsAsync(
            Guid.NewGuid(), new AdminGrantCreditsRequest(Staff, (decimal)credits, Now.AddDays(days)), CancellationToken.None);

        Assert.Equal(days is 0 or 367 ? "CREDITS_EXPIRES_AT_INVALID" : "CREDITS_INVALID", result.Error.Code);
        Assert.Null(runner.Action);
    }

    [Fact]
    public async Task Staff_credits_are_an_outbox_event_named_by_the_idempotency_key()
    {
        var tenant = new TenantPlan(12, Guid.NewGuid(), PlanCode.Pro, null, 3, null);
        var runner = new RecordingRunner();
        var outbox = new RecordingOutbox();
        var expiresAt = Now.AddDays(90);

        var result = await Service(Mock.Of<ITenantPlanService>(), runner: runner, tenants: Tenants(tenant), outbox: outbox)
            .GrantCreditsAsync(tenant.TenantRefId, new AdminGrantCreditsRequest(Staff, 5000m, expiresAt), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new StaffAction(AdminTenantService.GrantCreditsAction, AdminTenantService.TargetKind, tenant.TenantRefId), runner.Action);
        var message = Assert.Single(outbox.Messages);
        Assert.Equal(typeof(TenantCreditsGrantedEvent).FullName, message.MessageType);
        using var json = JsonDocument.Parse(message.Payload);
        Assert.Equal(12, json.RootElement.GetProperty("tenantId").GetInt32());
        Assert.Equal(5000m, json.RootElement.GetProperty("credits").GetDecimal());
        Assert.Equal(StaffCreditGrants.Source, json.RootElement.GetProperty("source").GetString());
        Assert.Equal($"{tenant.TenantRefId:D}/{Staff.IdempotencyKey:D}", json.RootElement.GetProperty("sourceRef").GetString());
    }

    [Fact]
    public async Task Revoking_names_only_this_tenants_staff_grant()
    {
        var tenant = new TenantPlan(12, Guid.NewGuid(), PlanCode.Pro, null, 3, null);
        var grantRef = Guid.NewGuid();
        var outbox = new RecordingOutbox();

        var result = await Service(Mock.Of<ITenantPlanService>(), runner: new RecordingRunner(), tenants: Tenants(tenant), outbox: outbox)
            .RevokeCreditGrantAsync(tenant.TenantRefId, grantRef, new AdminRevokeCreditGrantRequest(Staff), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var message = Assert.Single(outbox.Messages);
        Assert.Equal(typeof(TenantCreditGrantRevokedEvent).FullName, message.MessageType);
        using var json = JsonDocument.Parse(message.Payload);
        Assert.Equal(StaffCreditGrants.Source, json.RootElement.GetProperty("source").GetString());
        Assert.Equal($"{tenant.TenantRefId:D}/{grantRef:D}", json.RootElement.GetProperty("sourceRef").GetString());
    }

    [Fact]
    public async Task Credits_for_an_unknown_tenant_are_refused_and_send_nothing()
    {
        var outbox = new RecordingOutbox();

        var result = await Service(Mock.Of<ITenantPlanService>(), runner: new RecordingRunner(), tenants: Tenants(null), outbox: outbox)
            .GrantCreditsAsync(Guid.NewGuid(), new AdminGrantCreditsRequest(Staff, 10m, Now.AddDays(1)), CancellationToken.None);

        Assert.Equal("TENANT_NOT_FOUND", result.Error.Code);
        Assert.Empty(outbox.Messages);
    }

    [Theory]
    [InlineData(PlanSource.Subscription, 30)]
    [InlineData(PlanSource.Operator, -1)]
    public async Task Only_a_staff_plan_still_running_can_be_ended(PlanSource source, int endsInDays)
    {
        var tenant = new TenantPlan(12, Guid.NewGuid(), PlanCode.Team, null, 3, null, source, Now.AddDays(endsInDays).UtcDateTime);
        var review = new Mock<ITenantPlanReview>(MockBehavior.Strict);

        var result = await Service(new Mock<ITenantPlanService>(MockBehavior.Strict).Object, runner: new RecordingRunner(), tenants: Tenants(tenant), review: review.Object)
            .EndStaffPlanAsync(tenant.TenantRefId, new AdminEndStaffPlanRequest(Staff, 3), CancellationToken.None);

        Assert.Equal("STAFF_PLAN_NOT_ACTIVE", result.Error.Code);
    }

    [Fact]
    public async Task Ending_a_staff_plan_ends_it_now_and_asks_billing_to_move_the_tenant()
    {
        var overrides = new PlanLimitOverrides { Clients = 900 };
        var tenant = new TenantPlan(12, Guid.NewGuid(), PlanCode.Team, overrides, 3, null, PlanSource.Operator, Now.AddDays(10).UtcDateTime);
        var plans = new Mock<ITenantPlanService>();
        plans.Setup(p => p.SetAsync(tenant.TenantRefId, It.IsAny<SetTenantPlanRequest>(), PlanSource.Operator, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantPlanResponse(tenant.TenantRefId, PlanCode.Team, overrides, 4, Now, PlanSource.Operator, Now));
        var review = new Mock<ITenantPlanReview>();
        review.Setup(r => r.ReviewAsync(12, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("provider down"));
        var runner = new RecordingRunner();

        var result = await Service(plans.Object, runner: runner, tenants: Tenants(tenant), review: review.Object)
            .EndStaffPlanAsync(tenant.TenantRefId, new AdminEndStaffPlanRequest(Staff, 3), CancellationToken.None);

        // The provider being down does not undo the end: the hourly review moves the tenant instead.
        Assert.True(result.IsSuccess);
        Assert.Equal(AdminTenantService.EndPlanAction, runner.Action!.Name);
        plans.Verify(p => p.SetAsync(tenant.TenantRefId, new SetTenantPlanRequest(PlanCode.Team, overrides, 3, Now, null), PlanSource.Operator, It.IsAny<CancellationToken>()));
        review.Verify(r => r.ReviewAsync(12, It.IsAny<CancellationToken>()));
    }

    private static ITenantPlanProvider Tenants(TenantPlan? tenant)
    {
        var tenants = new Mock<ITenantPlanProvider>();
        tenants.Setup(t => t.FindByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        return tenants.Object;
    }

    private static AdminTenantService Service(
        ITenantPlanService plans,
        ITenantLimits? limits = null,
        IStaffActionRunner? runner = null,
        ITenantPlanProvider? tenants = null,
        IOutboxWriter? outbox = null,
        ITenantPlanReview? review = null) =>
        new(plans, limits ?? new FixedTenantLimits(), runner ?? Mock.Of<IStaffActionRunner>(MockBehavior.Strict), new FixedClock(Now),
            tenants ?? Mock.Of<ITenantPlanProvider>(MockBehavior.Strict), new NoWorkspaces(), outbox ?? new RecordingOutbox(),
            review ?? Mock.Of<ITenantPlanReview>(), NullLogger<AdminTenantService>.Instance);

    private sealed class NoWorkspaces : IAuditWorkspaces
    {
        public Task<IReadOnlyList<int>> OfTenantAsync(int tenantId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<int>>([7]);

        public Task<IReadOnlyList<int>> OfUserAsync(int userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<int>>([]);
    }

    private sealed class RecordingOutbox : IOutboxWriter
    {
        public List<OutboxMessage> Messages { get; } = [];

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Runs the change at once and remembers what it was asked to audit.</summary>
    private sealed class RecordingRunner : IStaffActionRunner
    {
        public StaffChange? Staff { get; private set; }

        public StaffAction? Action { get; private set; }

        public async Task<Result<StaffActionResponse>> RunAsync(StaffChange staff, StaffAction action, Func<CancellationToken, Task<Result>> change, CancellationToken cancellationToken)
        {
            Staff = staff;
            Action = action;
            var outcome = await change(cancellationToken);
            return outcome.IsFailure ? outcome.Error : new StaffActionResponse(1, false);
        }
    }

    private static AuthorizationFilterContext FilterContext(string? adminHeader, string? configured, string? internalHeader = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AdminApiKeyAttribute.ConfigKey] = configured,
                [InternalApiKeyAttribute.ConfigKey] = SharedKey
            })
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        if (adminHeader is not null)
        {
            http.Request.Headers[InternalRoutes.AdminApiKeyHeader] = adminHeader;
        }

        if (internalHeader is not null)
        {
            http.Request.Headers[InternalRoutes.ApiKeyHeader] = internalHeader;
        }

        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
    }
}
