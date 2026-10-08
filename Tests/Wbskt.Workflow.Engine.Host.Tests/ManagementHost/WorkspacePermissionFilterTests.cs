using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives.Constants;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// <see cref="WorkspacePermissionFilter"/> is now the only thing between a caller and a
/// workspace-scoped action, so its rules are pinned here: all-of, any-of, stacked attributes, an
/// action overriding its controller, and the answers for a workspace the caller cannot use.
/// </summary>
public sealed class WorkspacePermissionFilterTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task A_caller_with_the_permission_passes_and_the_action_gets_the_workspace_id()
    {
        var (context, http) = Context(nameof(FakeController.OneRead), PermissionNames.ClientsRead);

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        WorkspacePermissionFilter.TryGetWorkspaceId(http, out var id).Should().BeTrue();
        id.Should().Be(WorkspaceId);
    }

    [Fact]
    public async Task A_caller_without_the_permission_is_a_403_naming_it()
    {
        var (context, http) = Context(nameof(FakeController.OneRead), PermissionNames.LogsRead);

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        Forbidden(context).Message.Should().Contain(PermissionNames.ClientsRead);
        WorkspacePermissionFilter.TryGetWorkspaceId(http, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(new[] { PermissionNames.ClientsRead, PermissionNames.LogsRead }, true)]
    [InlineData(new[] { PermissionNames.ClientsRead }, false)]
    [InlineData(new string[0], false)]
    public async Task All_mode_needs_every_permission(string[] held, bool passes)
    {
        var (context, _) = Context(nameof(FakeController.AllOfTwo), held);

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        if (passes)
        {
            context.Result.Should().BeNull();
        }
        else
        {
            Forbidden(context);
        }
    }

    [Theory]
    [InlineData(new[] { PermissionNames.ClientsRead }, true)]
    [InlineData(new[] { PermissionNames.LogsRead }, true)]
    [InlineData(new[] { PermissionNames.ClientsRead, PermissionNames.LogsRead }, true)]
    [InlineData(new[] { PermissionNames.ClientsPing }, false)]
    public async Task Any_mode_needs_one_of_them(string[] held, bool passes)
    {
        var (context, _) = Context(nameof(FakeController.AnyOfTwo), held);

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        if (passes)
        {
            context.Result.Should().BeNull();
        }
        else
        {
            Forbidden(context).Message.Should().Contain("one of");
        }
    }

    [Theory]
    [InlineData(new[] { PermissionNames.ClientsUpdate, PermissionNames.LogsRead }, true)]
    [InlineData(new[] { PermissionNames.ClientsUpdate, PermissionNames.ClientsRead }, true)]
    [InlineData(new[] { PermissionNames.LogsRead, PermissionNames.ClientsRead }, false)]
    [InlineData(new[] { PermissionNames.ClientsUpdate }, false)]
    public async Task Stacked_attributes_must_all_pass(string[] held, bool passes)
    {
        // [RequiresPermission(ClientsUpdate)] and [RequiresPermission(ClientsRead, LogsRead, Any)]
        var (context, _) = Context(nameof(FakeController.UpdateAndOneRead), held);

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        if (passes)
        {
            context.Result.Should().BeNull();
        }
        else
        {
            Forbidden(context);
        }
    }

    [Fact]
    public async Task An_action_without_its_own_attribute_takes_its_controllers()
    {
        var (context, _) = Context(nameof(FakeController.Inherits), PermissionNames.ClientsRead);

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        Forbidden(context).Message.Should().Contain(PermissionNames.WorkflowsRead);
    }

    [Fact]
    public async Task An_actions_own_attribute_replaces_its_controllers()
    {
        // OneRead needs ClientsRead only, though the controller asks for WorkflowsRead.
        var (context, _) = Context(nameof(FakeController.OneRead), PermissionNames.ClientsRead);

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task A_workspace_the_caller_cannot_use_is_answered_as_the_resolve_answered_it()
    {
        var (context, _) = Context(nameof(FakeController.OneRead),
            resolve: Result<WorkspaceAccess>.Failure(Error.Forbidden("WORKSPACE_FORBIDDEN", "Access denied to workspace.")));

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        Forbidden(context).Code.Should().Be("WORKSPACE_FORBIDDEN");
    }

    [Fact]
    public async Task An_unidentified_caller_stays_a_401()
    {
        var (context, _) = Context(nameof(FakeController.OneRead),
            resolve: Result<WorkspaceAccess>.Failure(Error.Unauthorized("WORKSPACE_UNAUTHENTICATED", "Not authenticated.")));

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task An_action_without_requirements_is_left_alone_and_resolves_nothing()
    {
        var auth = new Mock<IAuthServiceClient>(MockBehavior.Strict);
        var (context, _) = Context(nameof(FakeOpenController.Open), auth: auth, controller: typeof(FakeOpenController));

        await new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task A_requirement_on_a_route_without_a_workspace_is_a_wiring_error()
    {
        var (context, _) = Context(nameof(FakeController.OneRead), PermissionNames.ClientsRead);
        context.RouteData.Values.Remove(WorkspacePermissionFilter.WorkspaceRefRouteKey);

        var act = () => new WorkspacePermissionFilter().OnAuthorizationAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void An_attribute_must_name_a_permission()
    {
        var act = () => new RequiresPermissionAttribute();

        act.Should().Throw<ArgumentException>();
    }

    private static Error Forbidden(AuthorizationFilterContext context)
    {
        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        return result.Value.Should().BeOfType<Error>().Subject;
    }

    private static (AuthorizationFilterContext Context, HttpContext Http) Context(
        string action,
        params string[] held)
    {
        return Context(action, resolve: Result<WorkspaceAccess>.Success(new WorkspaceAccess(WorkspaceId, held.ToHashSet(StringComparer.OrdinalIgnoreCase))));
    }

    private static (AuthorizationFilterContext Context, HttpContext Http) Context(
        string action,
        Result<WorkspaceAccess>? resolve = null,
        Mock<IAuthServiceClient>? auth = null,
        Type? controller = null)
    {
        controller ??= typeof(FakeController);
        if (auth is null)
        {
            auth = new Mock<IAuthServiceClient>();
            auth.Setup(a => a.ResolveWorkspaceAsync(WorkspaceRef, It.IsAny<CancellationToken>())).ReturnsAsync(resolve!);
        }

        var services = new ServiceCollection()
            .AddSingleton(auth.Object)
            .AddSingleton<ILogger<WorkspacePermissionFilter>>(NullLogger<WorkspacePermissionFilter>.Instance)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var routeData = new RouteData();
        routeData.Values[WorkspacePermissionFilter.WorkspaceRefRouteKey] = WorkspaceRef.ToString();
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = controller.GetMethod(action)!,
            ControllerTypeInfo = controller.GetTypeInfo()
        };

        var context = new AuthorizationFilterContext(new ActionContext(http, routeData, descriptor), []);
        return (context, http);
    }

    [RequiresPermission(PermissionNames.WorkflowsRead)]
    private sealed class FakeController : ControllerBase
    {
        [RequiresPermission(PermissionNames.ClientsRead)]
        public void OneRead() { }

        [RequiresPermission(PermissionNames.ClientsRead, PermissionNames.LogsRead)]
        public void AllOfTwo() { }

        [RequiresPermission(PermissionNames.ClientsRead, PermissionNames.LogsRead, Mode = PermissionMatch.Any)]
        public void AnyOfTwo() { }

        [RequiresPermission(PermissionNames.ClientsUpdate)]
        [RequiresPermission(PermissionNames.ClientsRead, PermissionNames.LogsRead, Mode = PermissionMatch.Any)]
        public void UpdateAndOneRead() { }

        public void Inherits() { }
    }

    private sealed class FakeOpenController : ControllerBase
    {
        public void Open() { }
    }
}
