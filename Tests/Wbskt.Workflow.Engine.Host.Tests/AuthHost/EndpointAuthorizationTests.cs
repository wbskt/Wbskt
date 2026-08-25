using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// Guards the default-deny posture on the two hosts that historically relied on attributes alone.
///
/// Both now set an authorization <c>FallbackPolicy</c>, so an un-attributed endpoint is refused at
/// runtime rather than served anonymously. That fallback is the safety net; this test makes the
/// intent explicit, because a fallback policy is easy to delete without noticing and nothing else in
/// the suite would fail if someone did.
///
/// The rule asserted is deliberately "every action states its posture", not "every action is
/// authorized" — <c>AuthController</c>'s credential endpoints must stay anonymous. What must never
/// happen is an endpoint whose posture was never stated at all.
/// </summary>
public sealed class EndpointAuthorizationTests
{
    /// <summary>
    /// Both hosts in one data set rather than a theory each: xunit fails a theory whose data set is
    /// empty, and the Socket host has no controllers at all. That emptiness is asserted directly
    /// below instead.
    /// </summary>
    public static TheoryData<Type> GuardedControllers =>
        ToTheoryData(ControllersIn(AuthHostAssembly).Concat(ControllersIn(SocketHostAssembly)));

    private static Assembly AuthHostAssembly => typeof(global::Wbskt.Auth.Host.Program).Assembly;

    private static Assembly SocketHostAssembly => typeof(global::Wbskt.Socket.Host.Program).Assembly;

    [Theory]
    [MemberData(nameof(GuardedControllers))]
    public void Every_action_states_its_authorization(Type controller)
    {
        AssertEveryActionIsAttributed(controller);
    }

    /// <summary>
    /// The Socket host's only routed surface is <c>/ws</c>, mapped directly and authenticated by
    /// <c>WebSocketAuthMiddleware</c> before the authorization pipeline runs — which is why that one
    /// endpoint opts out of the host's fallback policy with <c>AllowAnonymous</c>. A controller
    /// appearing here means that reasoning no longer covers the whole host, and the new endpoint
    /// needs its own posture stated.
    /// </summary>
    [Fact]
    public void Socket_host_has_no_controllers()
    {
        Assert.Empty(ControllersIn(SocketHostAssembly));
    }

    /// <summary>
    /// The Auth host owns tenants, roles and permissions, so an accidentally anonymous controller
    /// there is the worst case. Pinning the discovered set stops the theory above from silently
    /// degrading into a no-op if the reflection ever stops finding anything.
    /// </summary>
    [Fact]
    public void Auth_host_exposes_the_controllers_this_suite_expects()
    {
        string[] names = ControllersIn(AuthHostAssembly).Select(type => type.Name).ToArray();

        Assert.Equal(
            ["AuthController", "InvitationsController", "ManagementController", "WorkspacesController"],
            names);
    }

    private static void AssertEveryActionIsAttributed(Type controller)
    {
        bool classDeclares = Declares(controller);

        foreach (MethodInfo action in ActionsOn(controller))
        {
            Assert.True(
                classDeclares || Declares(action),
                $"{controller.Name}.{action.Name} declares neither [Authorize] nor [AllowAnonymous], on the "
                + "method or on its class. State one explicitly rather than inheriting whatever the host's "
                + "default happens to be.");
        }
    }

    private static bool Declares(MemberInfo member) =>
        member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
        || member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

    // DeclaredOnly so inherited ControllerBase helpers are not mistaken for actions; IsSpecialName
    // filters out property accessors and other compiler-generated members.
    private static IEnumerable<MethodInfo> ActionsOn(Type controller) =>
        controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);

    private static Type[] ControllersIn(Assembly assembly) =>
        assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

    private static TheoryData<Type> ToTheoryData(IEnumerable<Type> types)
    {
        var data = new TheoryData<Type>();

        foreach (Type type in types)
        {
            data.Add(type);
        }

        return data;
    }
}
