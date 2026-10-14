using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Auth.Host.Controllers;
using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Constants;
using Wbskt.Host.Tests.Infrastructure;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// <see cref="TenantPermissionFilter"/> is the only thing between a caller and a tenant
/// administration action, so the service methods behind them no longer check. Its answers are
/// pinned here, and so is which permission each sensitive action asks for.
/// </summary>
public sealed class TenantPermissionFilterTests
{
    private const int CallerId = 1;
    private const int TenantId = 5;
    private static readonly Guid TenantRef = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Mock<ITenantProvider> _tenants = new();
    private readonly Mock<IAccessControlProvider> _accessControl = new();

    public TenantPermissionFilterTests()
    {
        _tenants.Setup(p => p.FindTenantIdByRefIdForUserAsync(TenantRef, CallerId, It.IsAny<CancellationToken>())).ReturnsAsync(TenantId);
    }

    [Fact]
    public async Task A_caller_with_the_permission_tenant_wide_passes_and_the_action_gets_the_tenant_id()
    {
        Grant(PermissionNames.UsersManage);
        var (context, http) = Context(nameof(ManagementController.CreateInvitation));

        await new TenantPermissionFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        TenantPermissionFilter.TryGetScopeId(http, out var id).Should().BeTrue();
        id.Should().Be(TenantId);
    }

    /// <summary>
    /// Without <c>users.manage</c> the invitation action never runs, so no invitation is stored and
    /// no mail goes to an address the caller chose.
    /// </summary>
    [Fact]
    public async Task A_caller_without_the_permission_is_a_403_naming_it()
    {
        Grant(PermissionNames.UsersRead);
        var (context, http) = Context(nameof(ManagementController.CreateInvitation));

        await new TenantPermissionFilter().OnAuthorizationAsync(context);

        Forbidden(context).Message.Should().Contain(PermissionNames.UsersManage);
        TenantPermissionFilter.TryGetScopeId(http, out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_tenant_the_caller_does_not_belong_to_reads_as_not_found()
    {
        _tenants.Setup(p => p.FindTenantIdByRefIdForUserAsync(TenantRef, CallerId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var (context, _) = Context(nameof(ManagementController.GetMembers));

        await new TenantPermissionFilter().OnAuthorizationAsync(context);

        Forbidden(context).Code.Should().Be("TENANT_NOT_FOUND");
        _accessControl.Verify(p => p.VerifyPermissionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Only_a_tenant_wide_grant_counts()
    {
        // A grant in one workspace of the tenant does not make its holder a tenant administrator.
        _accessControl.Setup(p => p.VerifyPermissionAsync(CallerId, TenantId, It.Is<int?>(w => w != null), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var (context, _) = Context(nameof(ManagementController.GetMembers));

        await new TenantPermissionFilter().OnAuthorizationAsync(context);

        Forbidden(context);
    }

    [Fact]
    public async Task An_unidentified_caller_is_a_401()
    {
        var (context, _) = Context(nameof(ManagementController.GetMembers), identified: false);

        await new TenantPermissionFilter().OnAuthorizationAsync(context);

        ProblemAssert.Is(context.Result, StatusCodes.Status401Unauthorized);
    }

    [Theory]
    [InlineData(nameof(ManagementController.UpdateTenant), PermissionNames.UsersManage)]
    [InlineData(nameof(ManagementController.CreateInvitation), PermissionNames.UsersManage)]
    [InlineData(nameof(ManagementController.RemoveMember), PermissionNames.UsersManage)]
    [InlineData(nameof(ManagementController.SetMemberSuspended), PermissionNames.UsersManage)]
    [InlineData(nameof(ManagementController.GetMembers), PermissionNames.UsersRead)]
    [InlineData(nameof(ManagementController.GetRoles), PermissionNames.RolesRead)]
    [InlineData(nameof(ManagementController.CreateRole), PermissionNames.RolesManage)]
    [InlineData(nameof(ManagementController.GrantRolePermission), PermissionNames.RolesManage)]
    [InlineData(nameof(ManagementController.AssignUserRole), PermissionNames.RolesManage)]
    [InlineData(nameof(ManagementController.GrantUserPermission), PermissionNames.RolesManage)]
    public void Each_tenant_action_asks_for_its_permission(string action, string permission)
    {
        var requirements = TenantPermissionFilter.RequirementsFor(typeof(ManagementController).GetMethod(action)!, typeof(ManagementController));

        requirements.SelectMany(r => r.Permissions).Should().Equal(permission);
    }

    [Fact]
    public void Every_action_under_a_tenant_declares_a_tenant_permission()
    {
        var unguarded = typeof(ManagementController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any(a => a.Template?.Contains("{tenantRef}") == true))
            .Where(m => TenantPermissionFilter.RequirementsFor(m, typeof(ManagementController)).Count == 0)
            .Select(m => m.Name);

        unguarded.Should().BeEmpty();
    }

    private void Grant(params string[] permissions)
    {
        foreach (string permission in permissions)
        {
            _accessControl.Setup(p => p.VerifyPermissionAsync(CallerId, TenantId, null, permission, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }
    }

    private static Error Forbidden(AuthorizationFilterContext context)
    {
        string code = ProblemAssert.Is(context.Result, StatusCodes.Status403Forbidden);
        return Error.Forbidden(code, ProblemAssert.Detail(context.Result));
    }

    private (AuthorizationFilterContext Context, HttpContext Http) Context(string action, bool identified = true)
    {
        var identity = new Mock<IIdentityService>();
        UserIdentity? caller = identified ? new UserIdentity(CallerId) : null;
        identity.Setup(i => i.TryGetUserIdentity(out caller)).Returns(identified);

        var services = new ServiceCollection()
            .AddSingleton(identity.Object)
            .AddSingleton(_tenants.Object)
            .AddSingleton(_accessControl.Object)
            .AddLogging()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var routeData = new RouteData();
        routeData.Values[TenantPermissionFilter.TenantRefRouteKey] = TenantRef.ToString();
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = typeof(ManagementController).GetMethod(action)!,
            ControllerTypeInfo = typeof(ManagementController).GetTypeInfo()
        };

        return (new AuthorizationFilterContext(new ActionContext(http, routeData, descriptor), []), http);
    }
}
