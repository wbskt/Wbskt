using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Primitives.Constants;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The auth host's tenant administration (<see cref="RequiresTenantPermissionAttribute"/>) and its
/// workspace routes (<see cref="RequiresPermissionAttribute"/>) declare what they need, so it can be
/// read and pinned from metadata, as <c>ManagementEndpointPermissionTests</c> does for the management
/// host. An action under a tenant or workspace reference without a requirement fails here instead of
/// shipping open to anyone who can name the reference.
/// </summary>
public sealed class AuthEndpointPermissionTests
{
    private static readonly string[] ScopedRouteSegments = ["{tenantRef", "{workspaceRef"];

    /// <summary>
    /// The scoped actions that need no permission, each because it is about the caller themself and
    /// checks membership in its service. Host-to-host actions carrying <see cref="InternalApiKeyAttribute"/>
    /// (or, for the platform admin host, <see cref="AdminApiKeyAttribute"/>)
    /// are not on this list: no user is behind them, and the key is their requirement.
    /// </summary>
    private static readonly HashSet<string> Unrestricted =
    [
        // Leaving a tenant: any member may, a suspended one included.
        "ManagementController.LeaveTenant",

        // What the caller may do in a workspace: asking cannot need a permission.
        "WorkspacesController.GetMyPermissions",
    ];

    /// <summary>What every scoped action requires. A change here is a change to who can do what.</summary>
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["ManagementController.AddUserToGroup"] = Tenant(PermissionNames.UsersManage),
        ["ManagementController.AssignGroupRole"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.AssignUserRole"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.CreateGroup"] = Tenant(PermissionNames.UsersManage),
        ["ManagementController.CreateInvitation"] = Tenant(PermissionNames.UsersManage),
        ["ManagementController.CreateRole"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.DeleteGroup"] = Tenant(PermissionNames.UsersManage),
        ["ManagementController.DeleteRole"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.GetGroupRoles"] = Tenant(PermissionNames.RolesRead),
        ["ManagementController.GetGroups"] = Tenant(PermissionNames.UsersRead),
        ["ManagementController.GetInvitations"] = Tenant(PermissionNames.UsersRead),
        ["ManagementController.GetMembers"] = Tenant(PermissionNames.UsersRead),
        ["ManagementController.GetPermissions"] = Tenant(PermissionNames.RolesRead),
        ["ManagementController.GetRolePermissions"] = Tenant(PermissionNames.RolesRead),
        ["ManagementController.GetRoles"] = Tenant(PermissionNames.RolesRead),
        ["ManagementController.GetUsage"] = Tenant(PermissionNames.BillingRead),
        ["ManagementController.GetUserGroups"] = Tenant(PermissionNames.UsersRead),
        ["ManagementController.GetUserPermissions"] = Tenant(PermissionNames.RolesRead),
        ["ManagementController.GetUserRoles"] = Tenant(PermissionNames.RolesRead),
        ["ManagementController.GrantRolePermission"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.GrantUserPermission"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.RemoveGroupRole"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.RemoveMember"] = Tenant(PermissionNames.UsersManage),
        ["ManagementController.RemoveRolePermission"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.RemoveUserFromGroup"] = Tenant(PermissionNames.UsersManage),
        ["ManagementController.RemoveUserPermission"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.RemoveUserRole"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.RevokeInvitation"] = Tenant(PermissionNames.UsersManage),
        ["ManagementController.UpdateGroup"] = Tenant(PermissionNames.UsersManage),
        ["ManagementController.UpdateMember"] = Tenant(PermissionNames.UsersManage),
        ["ManagementController.UpdateRole"] = Tenant(PermissionNames.RolesManage),
        ["ManagementController.UpdateTenant"] = Tenant(PermissionNames.UsersManage),
        ["TenantBillingController.ChangePlan"] = Tenant(PermissionNames.BillingManage),
        ["TenantBillingController.Checkout"] = Tenant(PermissionNames.BillingManage),
        ["TenantBillingController.Get"] = Tenant(PermissionNames.BillingRead),
        ["TenantBillingController.Portal"] = Tenant(PermissionNames.BillingManage),
        ["WorkspacesController.AddMember"] = Workspace(PermissionNames.UsersManage),
        ["WorkspacesController.DeleteWorkspace"] = Workspace(PermissionNames.UsersManage),
        ["WorkspacesController.GetMembers"] = Workspace(PermissionNames.UsersRead),
        ["WorkspacesController.RemoveMember"] = Workspace(PermissionNames.UsersManage),
        ["WorkspacesController.TransferOwnership"] = Workspace(PermissionNames.UsersManage),
        ["WorkspacesController.UpdateWorkspace"] = Workspace(PermissionNames.UsersManage),
    };

    [Fact]
    public void Every_tenant_or_workspace_scoped_action_declares_its_permissions()
    {
        var scoped = Actions().Where(IsScoped).ToList();
        var undeclared = scoped
            .Where(a => a.Tenant.Count == 0 && a.Workspace.Count == 0 && !a.InternalKey && !Unrestricted.Contains(a.Name))
            .Select(a => $"{a.Name} ({a.Route})")
            .ToList();

        scoped.Should().HaveCountGreaterThan(Expected.Count / 2, "the route filter must find the scoped actions, or this test checks nothing");
        undeclared.Should().BeEmpty("an action under a tenant or workspace reference needs a permission, or anyone who can name the reference could call it");
    }

    [Fact]
    public void Each_action_requires_exactly_the_permissions_it_always_has()
    {
        var actual = Actions()
            .Where(a => a.Tenant.Count > 0 || a.Workspace.Count > 0)
            .GroupBy(a => a.Name)
            .ToDictionary(g => g.Key, g => Describe(g.First()));

        actual.Should().BeEquivalentTo(Expected);
    }

    [Fact]
    public void Every_unrestricted_action_still_exists_and_still_needs_nothing()
    {
        var unrestricted = Actions().Where(a => Unrestricted.Contains(a.Name)).ToList();

        unrestricted.Select(a => a.Name).Distinct().Should().BeEquivalentTo(Unrestricted);
        unrestricted.Should().OnlyContain(a => a.Tenant.Count == 0 && a.Workspace.Count == 0);
    }

    private static bool IsScoped(Action action) =>
        ScopedRouteSegments.Any(segment => action.Route.Contains(segment, StringComparison.OrdinalIgnoreCase));

    private static string Tenant(string permission) => "tenant:" + permission;

    private static string Workspace(string permission) => "workspace:" + permission;

    private static string Describe(Action action)
    {
        var parts = action.Tenant.Select(r => "tenant:" + Describe(r)).Concat(action.Workspace.Select(r => "workspace:" + Describe(r)));
        return string.Join(" & ", parts);
    }

    private static string Describe(PermissionRequirementAttribute requirement) =>
        requirement.Permissions.Count == 1 ? requirement.Permissions[0] : $"{requirement.Mode}({string.Join(", ", requirement.Permissions)})";

    private sealed record Action(
        string Name,
        string Route,
        IReadOnlyList<RequiresTenantPermissionAttribute> Tenant,
        IReadOnlyList<RequiresPermissionAttribute> Workspace,
        bool InternalKey);

    private static IEnumerable<Action> Actions()
    {
        var controllers = typeof(global::Wbskt.Auth.Host.Program).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var prefixes = controller.GetCustomAttributes<RouteAttribute>().Select(r => r.Template).DefaultIfEmpty(string.Empty).ToList();
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<NonActionAttribute>() is null && !m.IsSpecialName);

            foreach (var method in methods)
            {
                var templates = method.GetCustomAttributes<HttpMethodAttribute>().Select(h => h.Template).DefaultIfEmpty(null).ToList();
                foreach (var prefix in prefixes)
                {
                    foreach (var template in templates)
                    {
                        var route = template is null ? prefix : template.StartsWith("api/") ? template : $"{prefix}/{template}";
                        yield return new Action(
                            $"{controller.Name}.{method.Name}",
                            route,
                            TenantPermissionFilter.RequirementsFor(method, controller),
                            WorkspacePermissionFilter.RequirementsFor(method, controller),
                            HasKey<InternalApiKeyAttribute>(controller, method) || HasKey<AdminApiKeyAttribute>(controller, method));
                    }
                }
            }
        }
    }

    private static bool HasKey<TAttribute>(Type controller, MethodInfo method)
        where TAttribute : Attribute =>
        controller.GetCustomAttribute<TAttribute>() is not null || method.GetCustomAttribute<TAttribute>() is not null;
}
