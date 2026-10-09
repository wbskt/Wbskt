using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Wbskt.Management.Host.Authorization;
using Wbskt.Primitives.Constants;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// Permissions on the management host are declared with <see cref="RequiresPermissionAttribute"/>
/// rather than checked by hand, so they can be read, and pinned, from metadata. A workspace-scoped
/// action without one fails here instead of shipping open to any member of the workspace.
/// </summary>
public sealed class ManagementEndpointPermissionTests
{
    private const string WorkspaceRoutePrefix = "api/workspaces/{workspaceRef";

    /// <summary>What every workspace-scoped action requires. A change here is a change to who can do what.</summary>
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["ClientReadingsController.Get"] = PermissionNames.ClientsRead,
        ["ClientReadingsController.GetCsv"] = PermissionNames.ClientsRead,
        ["ClientsController.GetAll"] = PermissionNames.ClientsRead,
        ["ClientsController.GetTags"] = PermissionNames.ClientsRead,
        ["ClientsController.GetByPolicy"] = PermissionNames.ClientsRead,
        ["ClientsController.GetDetail"] = PermissionNames.ClientsRead,
        ["ClientsController.Update"] = PermissionNames.ClientsUpdate,
        ["ClientsController.UpdateStatus"] = PermissionNames.ClientsUpdate,
        ["ClientsController.UpdateStatuses"] = PermissionNames.ClientsUpdate,
        ["ClientsController.Delete"] = PermissionNames.ClientsManage,
        ["ClientsController.RotateSecret"] = PermissionNames.ClientsManage,
        ["ClientsController.GetState"] = PermissionNames.ClientsRead,
        ["ClientsController.Rename"] = PermissionNames.ClientsUpdate,
        ["ClientsController.SetTags"] = PermissionNames.ClientsUpdate,
        ["ClientsController.SendCommand"] = PermissionNames.ClientsCommand,
        ["ClientsController.SendCommandLegacy"] = PermissionNames.ClientsCommand,
        ["ClientsController.GetComms"] = PermissionNames.LogsRead,
        ["ClientsController.Ping"] = PermissionNames.ClientsPing,
        ["EventLogsController.GetLogs"] = PermissionNames.LogsRead,
        ["MessageTemplatesController.GetAll"] = PermissionNames.TemplatesRead,
        ["MessageTemplatesController.Create"] = PermissionNames.TemplatesManage,
        ["MessageTemplatesController.Update"] = PermissionNames.TemplatesManage,
        ["MessageTemplatesController.Delete"] = PermissionNames.TemplatesManage,
        ["RegistrationPoliciesController.GetAll"] = PermissionNames.PoliciesRead,
        ["RegistrationPoliciesController.Get"] = PermissionNames.PoliciesRead,
        ["RegistrationPoliciesController.Create"] = PermissionNames.PoliciesManage,
        ["RegistrationPoliciesController.Update"] = PermissionNames.PoliciesManage,
        ["RegistrationPoliciesController.RotatePin"] = PermissionNames.PoliciesManage,
        ["RegistrationPoliciesController.Disable"] = PermissionNames.PoliciesManage,
        ["SharedVariablesController.Get"] = PermissionNames.WorkflowsRead,
        ["SharedVariablesController.Set"] = PermissionNames.WorkflowsExecute,
        ["WorkflowHistoryController.List"] = PermissionNames.WorkflowsRead,
        ["WorkflowRunsController.List"] = PermissionNames.WorkflowsRead,
        ["WorkflowRunsController.ListForWorkspace"] = PermissionNames.WorkflowsRead,
        ["WorkflowRunsController.GetWorkspaceStats"] = PermissionNames.WorkflowsRead,
        ["WorkflowRunsController.GetStats"] = PermissionNames.WorkflowsRead,
        ["WorkflowRunsController.Get"] = PermissionNames.WorkflowsRead,
        ["WorkflowRunsController.Cancel"] = PermissionNames.WorkflowsExecute,
        ["WorkflowRunsController.Signal"] = PermissionNames.WorkflowsExecute,
        ["WorkflowsController.Publish"] = PermissionNames.WorkflowsCreate,
        ["WorkflowsController.ValidateDefinition"] = PermissionNames.WorkflowsCreate,
        ["WorkflowsController.GetAll"] = PermissionNames.WorkflowsRead,
        ["WorkflowsController.GetCurrent"] = PermissionNames.WorkflowsRead,
        ["WorkflowsController.GetVersions"] = PermissionNames.WorkflowsRead,
        ["WorkflowsController.GetVersion"] = PermissionNames.WorkflowsRead,
        ["WorkflowsController.Deprecate"] = PermissionNames.WorkflowsDelete,
        ["WorkflowsController.Delete"] = PermissionNames.WorkflowsDelete,
        ["WorkflowsController.Reinstate"] = PermissionNames.WorkflowsDelete,
        ["WorkflowsController.Rollback"] = PermissionNames.WorkflowsCreate,
        ["WorkflowsController.StartManualRun"] = PermissionNames.WorkflowsExecute,
    };

    private static Assembly ManagementHostAssembly => typeof(global::Wbskt.Management.Host.Program).Assembly;

    [Fact]
    public void Every_workspace_scoped_action_declares_its_permissions()
    {
        var undeclared = Actions()
            .Where(a => a.Route.StartsWith(WorkspaceRoutePrefix, StringComparison.OrdinalIgnoreCase) && a.Requirements.Count == 0)
            .Select(a => $"{a.Name} ({a.Route})")
            .ToList();

        undeclared.Should().BeEmpty("a workspace-scoped action needs [RequiresPermission], or any member of the workspace could call it");
    }

    [Fact]
    public void Each_action_requires_exactly_the_permissions_it_always_has()
    {
        var actual = Actions()
            .Where(a => a.Requirements.Count > 0)
            .ToDictionary(a => a.Name, a => Describe(a.Requirements));

        actual.Should().BeEquivalentTo(Expected);
    }

    [Fact]
    public void Only_actions_with_requirements_bind_the_workspace_id()
    {
        var unbound = Actions()
            .Where(a => a.Requirements.Count == 0
                        && a.Method.GetParameters().Any(p => p.GetCustomAttribute<FromWorkspaceAttribute>() is not null))
            .Select(a => a.Name)
            .ToList();

        unbound.Should().BeEmpty("[FromWorkspace] is only bound once the filter has resolved the workspace");
    }

    [Fact]
    public void Every_permission_named_exists()
    {
        var known = typeof(PermissionNames).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        Actions().SelectMany(a => a.Requirements).SelectMany(r => r.Permissions)
            .Should().OnlyContain(p => known.Contains(p));
    }

    private static string Describe(IReadOnlyList<RequiresPermissionAttribute> requirements)
    {
        return string.Join(" & ", requirements.Select(r =>
            r.Permissions.Count == 1 ? r.Permissions[0] : $"{r.Mode}({string.Join(", ", r.Permissions)})"));
    }

    private sealed record Action(string Name, string Route, MethodInfo Method, IReadOnlyList<RequiresPermissionAttribute> Requirements);

    private static IEnumerable<Action> Actions()
    {
        var controllers = ManagementHostAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<NonActionAttribute>() is null && !m.IsSpecialName);

            foreach (var method in methods)
            {
                var templates = method.GetCustomAttributes<HttpMethodAttribute>().Select(h => h.Template).DefaultIfEmpty(null);
                foreach (var template in templates)
                {
                    var route = template is null ? prefix : template.StartsWith("api/") ? template : $"{prefix}/{template}";
                    yield return new Action($"{controller.Name}.{method.Name}", route, method,
                        WorkspacePermissionFilter.RequirementsFor(method, controller));
                }
            }
        }
    }
}
