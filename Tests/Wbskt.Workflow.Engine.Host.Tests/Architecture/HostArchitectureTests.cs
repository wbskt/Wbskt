using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Models;
using Wbskt.Primitives;
using Wbskt.Workflow.Engine.Host.Middleware;

namespace Wbskt.Workflow.Engine.Host.Tests.Architecture;

/// <summary>
/// The rules in Docs/Coding.Conventions.md that can be read from metadata, checked on every host's
/// controllers at once, so a rule a host was never tested for cannot drift there unnoticed.
/// </summary>
/// <remarks>
/// Known violations start on an allow-list next to their rule. A list only shrinks: an entry that no
/// longer violates the rule fails the test too, so the fix also removes it from the list.
/// </remarks>
public sealed partial class HostArchitectureTests
{
    private static readonly Assembly AuthHost = typeof(global::Wbskt.Auth.Host.Program).Assembly;
    private static readonly Assembly ManagementHost = typeof(global::Wbskt.Management.Host.Program).Assembly;
    private static readonly Assembly SocketHost = typeof(global::Wbskt.Socket.Host.Program).Assembly;
    private static readonly Assembly EngineHost = typeof(global::Wbskt.Workflow.Engine.Host.Program).Assembly;

    private static readonly Assembly[] PublicHosts = [AuthHost, ManagementHost, SocketHost];
    private static readonly Assembly[] AllHosts = [AuthHost, ManagementHost, SocketHost, EngineHost];

    /// <summary>Controllers that still reach a provider, the bus or a mapper themselves.</summary>
    private static readonly HashSet<string> LayeringExceptions = [];

    /// <summary>Route parameters that predate the naming rule (#301).</summary>
    private static readonly HashSet<string> RouteParameterExceptions = [];

    /// <summary>
    /// Lists that return something other than <see cref="Page{T}"/> (#301). These return bare arrays
    /// the console reads as arrays, so each moves with a console change.
    /// </summary>
    private static readonly HashSet<string> ListShapeExceptions =
    [
        "AuthController.GetSessions",
        "ManagementController.GetGroupRoles",
        "ManagementController.GetRolePermissions",
        "ManagementController.GetTenants",
        "ManagementController.GetUserGroups",
        "ManagementController.GetUserPermissions",
        "ManagementController.GetUserRoles",
        "WorkspacesController.GetWorkspaces",
    ];

    /// <summary>Ids that are public on purpose.</summary>
    private static readonly HashSet<string> InternalIdExceptions =
    [
        // The log's id is its public cursor (sinceId) and is unique per database, not per tenant.
        "ClientsController.GetComms:EventLogResponse.Id",
        "EventLogsController.GetLogs:EventLogResponse.Id",

        // A run's history position, which the caller pages with.
        "WorkflowHistoryController.List:HistoryEventDto.HistoryEventId",
    ];

    [Fact]
    public void Every_host_has_the_controllers_this_suite_expects()
    {
        // Pins the discovered set, so the rules below cannot pass by finding nothing.
        Assert.Equal(["AuthController", "InternalWorkspacesController", "InvitationsController", "ManagementController", "WorkspacesController"], Names(ControllersIn(AuthHost)));
        Assert.NotEmpty(ControllersIn(ManagementHost));
        Assert.NotEmpty(ControllersIn(EngineHost));

        // The socket host's only routed surface is /ws, mapped with the ClientOnly policy. A
        // controller appearing there needs its own posture stated first.
        Assert.Empty(ControllersIn(SocketHost));
    }

    [Fact]
    public void Every_public_action_states_its_authorization()
    {
        // The hosts also set a fallback policy, so an unmarked action is refused at runtime. The
        // fallback is easy to delete without noticing; this makes the intent explicit. The rule is
        // "states its posture", not "is authorized": sign-in must stay anonymous.
        var unstated = PublicHosts.SelectMany(ActionsIn)
            .Where(a => !DeclaresAuthorization(a.Controller) && !DeclaresAuthorization(a.Method))
            .Select(a => a.Name)
            .ToList();

        Assert.Empty(unstated);
    }

    [Fact]
    public void Every_engine_controller_is_an_inbound_endpoint()
    {
        // The engine's API-key check covers every endpoint that is not [AllowAnonymous]; the leader
        // check covers only [InboundEndpoint] ones. A controller without the attribute would be
        // served by a standby.
        var unmarked = ControllersIn(EngineHost)
            .Where(c => c.GetCustomAttribute<InboundEndpointAttribute>() is null)
            .Select(c => c.Name)
            .ToList();

        Assert.Empty(unmarked);
    }

    [Fact]
    public void No_controller_injects_an_event_bus_provider_or_reference_mapper()
    {
        // A controller binds, calls one service and maps the result. Publishing and reaching the
        // database belong to services, where the choice of the real or the queued bus is made once.
        var offending = AllHosts.SelectMany(ControllersIn)
            .Where(c => c.GetConstructors()
                .SelectMany(ctor => ctor.GetParameters())
                .Select(p => p.ParameterType)
                .Any(t => t == typeof(IEventBus) || t == typeof(IReferenceMapper) || t.Name.EndsWith("Provider", StringComparison.Ordinal)))
            .Select(c => c.Name);

        AssertOnlyKnown(offending, LayeringExceptions);
    }

    [Fact]
    public void Route_parameters_name_the_resource_and_match_the_argument()
    {
        // {clientRef}, {workflowRef}, {signalName}: never a bare {id}, {refId} or {name}, and the C#
        // parameter has the same name, so the route reads the same as the code.
        var offending = new List<string>();
        foreach (var action in AllHosts.SelectMany(ActionsIn))
        {
            var arguments = action.Method.GetParameters().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

            // The permission filters resolve {workspaceRef} and {tenantRef} for any action that
            // declares a permission, and bind the internal id as [FromWorkspace] / [FromTenant] if asked.
            if (ScopedPermissionFilter<RequiresPermissionAttribute>.RequirementsFor(action.Method, action.Controller).Count > 0)
            {
                arguments.Add("workspaceRef");
            }

            if (ScopedPermissionFilter<RequiresTenantPermissionAttribute>.RequirementsFor(action.Method, action.Controller).Count > 0)
            {
                arguments.Add("tenantRef");
            }

            foreach (string parameter in RouteParameters(action.Route))
            {
                if (parameter is "id" or "refId" or "name" || !arguments.Contains(parameter))
                {
                    offending.Add($"{action.Name}:{parameter}");
                }
            }
        }

        AssertOnlyKnown(offending, RouteParameterExceptions);
    }

    [Fact]
    public void A_list_returns_a_page()
    {
        // Only actions that declare their response type can be read here; an IActionResult is opaque.
        var offending = AllHosts.SelectMany(ActionsIn)
            .Where(a => DeclaredResponse(a.Method) is { } response && IsList(response))
            .Select(a => a.Name);

        AssertOnlyKnown(offending, ListShapeExceptions);
    }

    [Fact]
    public void No_declared_response_carries_an_internal_id()
    {
        // Callers name things by RefId. An int Id leaks row counts and is only meaningful inside one
        // database, so a response names things by RefId only. Routes under InternalRoutes.Prefix are
        // host-to-host and never routed publicly, so an internal id is their point.
        var offending = new List<string>();
        foreach (var action in AllHosts.SelectMany(ActionsIn).Where(a => !a.Route.StartsWith(InternalRoutes.Prefix, StringComparison.Ordinal)))
        {
            if (DeclaredResponse(action.Method) is { } response)
            {
                offending.AddRange(InternalIds(response, new HashSet<Type>()).Select(path => $"{action.Name}:{path}"));
            }
        }

        AssertOnlyKnown(offending, InternalIdExceptions);
    }

    private static void AssertOnlyKnown(IEnumerable<string> found, HashSet<string> known)
    {
        var actual = found.Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(known.Order(StringComparer.Ordinal), actual);
    }

    private static bool DeclaresAuthorization(MemberInfo member) =>
        member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
        || member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

    private static IEnumerable<string> RouteParameters(string route) =>
        RouteParameterPattern().Matches(route).Select(m => m.Groups["name"].Value);

    /// <summary>The T of <c>Task&lt;ActionResult&lt;T&gt;&gt;</c>, <c>ActionResult&lt;T&gt;</c> or <c>Task&lt;T&gt;</c>.</summary>
    private static Type? DeclaredResponse(MethodInfo method)
    {
        Type type = method.ReturnType;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            type = type.GetGenericArguments()[0];
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ActionResult<>))
        {
            return type.GetGenericArguments()[0];
        }

        return typeof(IActionResult).IsAssignableFrom(type) || type == typeof(Task) || type == typeof(void) ? null : type;
    }

    private static bool IsList(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Page<>))
        {
            return false;
        }

        return type != typeof(string) && (typeof(IEnumerable).IsAssignableFrom(type) || type.Name.StartsWith("ListResponse", StringComparison.Ordinal));
    }

    private static IEnumerable<string> InternalIds(Type type, HashSet<Type> seen)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true && !type.IsGenericType)
        {
            yield break;
        }

        if (type.IsArray)
        {
            foreach (string path in InternalIds(type.GetElementType()!, seen))
            {
                yield return path;
            }

            yield break;
        }

        if (type.IsGenericType)
        {
            foreach (string path in type.GetGenericArguments().SelectMany(t => InternalIds(t, seen)))
            {
                yield return path;
            }

            if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            {
                yield break;
            }
        }

        if (!seen.Add(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null))
        {
            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if ((propertyType == typeof(int) || propertyType == typeof(long)) && property.Name.EndsWith("Id", StringComparison.Ordinal))
            {
                yield return $"{type.Name}.{property.Name}";
            }
            else
            {
                foreach (string path in InternalIds(property.PropertyType, seen))
                {
                    yield return path;
                }
            }
        }
    }

    private static string[] Names(IEnumerable<Type> types) => types.Select(t => t.Name).ToArray();

    private static Type[] ControllersIn(Assembly assembly) =>
        assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

    internal sealed record Action(string Name, Type Controller, MethodInfo Method, string Route);

    internal static IEnumerable<Action> ActionsIn(Assembly assembly)
    {
        foreach (var controller in ControllersIn(assembly))
        {
            string prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

            // DeclaredOnly so ControllerBase helpers are not mistaken for actions; IsSpecialName
            // drops property accessors.
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

            foreach (var method in methods)
            {
                var templates = method.GetCustomAttributes<HttpMethodAttribute>().Select(h => h.Template)
                    .Concat(method.GetCustomAttributes<RouteAttribute>().Select(r => r.Template))
                    .DefaultIfEmpty(null);
                foreach (string? template in templates)
                {
                    string route = template is null ? prefix
                        : template.StartsWith('/') || template.StartsWith("api/", StringComparison.Ordinal) ? template
                        : $"{prefix}/{template}";
                    yield return new Action($"{controller.Name}.{method.Name}", controller, method, route);
                }
            }
        }
    }

    // {name}, {name:guid}, {name?}, {*name}, {**name}
    [GeneratedRegex(@"\{\**(?<name>[A-Za-z0-9_]+)[^}]*\}")]
    private static partial Regex RouteParameterPattern();
}
