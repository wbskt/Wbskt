using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wbskt.Auth.Host.Controllers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Host.Tests.Infrastructure;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Infrastructure.Billing;
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

        var result = await new AdminTenantService(plans.Object, limits).GetLimitsAsync(tenantRef, CancellationToken.None);

        Assert.Equal(new AdminTenantLimitsResponse(plan, limits.Limits), result.Value);
    }

    [Fact]
    public async Task An_unknown_tenant_is_the_plan_services_not_found()
    {
        var plans = new Mock<ITenantPlanService>();
        plans.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.NotFound("TENANT_NOT_FOUND", "No tenant."));

        var result = await new AdminTenantService(plans.Object, new FixedTenantLimits()).GetLimitsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("TENANT_NOT_FOUND", result.Error.Code);
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
