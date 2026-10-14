using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Auth.Host.Controllers;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Host.Tests.Infrastructure;
using Wbskt.Infrastructure;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// <c>GET /api/internal/workspaces/deleted</c>: the list the management host's deleted workspace sweep
/// reads. No user is behind that call, so the shared internal key is its only gate, and it is pinned
/// here along with the service and the action.
/// </summary>
public sealed class DeletedWorkspacesTests
{
    private const string Key = "internal-key";
    private static readonly DateTime Since = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Until = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IWorkspaceProvider> _workspaces = new();

    [Fact]
    public async Task The_service_returns_the_workspaces_deleted_in_the_window()
    {
        _workspaces.Setup(p => p.GetDeletedBetweenAsync(Since, Until, It.IsAny<CancellationToken>())).ReturnsAsync([4, 9]);

        var result = await Service().GetDeletedBetweenAsync(Since, Until);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 4, 9 }, result.Value);
    }

    [Fact]
    public async Task A_reversed_or_empty_window_is_refused_without_a_query()
    {
        var reversed = await Service().GetDeletedBetweenAsync(Until, Since);
        var empty = await Service().GetDeletedBetweenAsync(Since, Since);

        Assert.Equal("DELETED_WINDOW_INVALID", reversed.Error.Code);
        Assert.Equal("DELETED_WINDOW_INVALID", empty.Error.Code);
        _workspaces.Verify(p => p.GetDeletedBetweenAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_action_answers_the_ids_and_passes_the_window_in_utc()
    {
        var service = new Mock<IWorkspaceService>();
        service.Setup(s => s.GetDeletedBetweenAsync(Since, Until, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<int>>.Success([4, 9]));
        var controller = new InternalWorkspacesController(service.Object, new AuthMetrics());

        // An offset other than UTC still names the same instant.
        var since = new DateTimeOffset(Since).ToOffset(TimeSpan.FromHours(5));
        var response = await controller.GetDeleted(since, new DateTimeOffset(Until), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Equal(new[] { 4, 9 }, Assert.IsType<DeletedWorkspacesResponse>(ok.Value).WorkspaceIds);
    }

    [Fact]
    public void The_action_takes_the_internal_key_instead_of_a_token()
    {
        MethodInfo action = typeof(InternalWorkspacesController).GetMethod(nameof(InternalWorkspacesController.GetDeleted))!;

        Assert.NotNull(action.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(action.GetCustomAttribute<InternalApiKeyAttribute>());
        Assert.Equal(InternalRoutes.DeletedWorkspaces, "api/internal/workspaces/" + action.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public void A_call_without_the_right_key_is_refused(string? sent)
    {
        var context = FilterContext(sent, configured: Key, Environments.Production);

        new InternalApiKeyAttribute().OnAuthorization(context);

        Assert.Equal("INTERNAL_API_KEY_INVALID", ProblemAssert.Is(context.Result, StatusCodes.Status401Unauthorized));
    }

    [Fact]
    public void The_right_key_is_let_through()
    {
        var context = FilterContext(Key, configured: Key, Environments.Production);

        new InternalApiKeyAttribute().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Production_without_a_configured_key_refuses_every_call()
    {
        var context = FilterContext(Key, configured: null, Environments.Production);

        new InternalApiKeyAttribute().OnAuthorization(context);

        Assert.Equal("INTERNAL_API_KEY_NOT_CONFIGURED", ProblemAssert.Is(context.Result, StatusCodes.Status503ServiceUnavailable));
    }

    [Fact]
    public void Development_without_a_configured_key_lets_calls_through()
    {
        var context = FilterContext(sent: null, configured: null, Environments.Development);

        new InternalApiKeyAttribute().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    private WorkspaceService Service() => new(
        _workspaces.Object, Mock.Of<IAccountProvider>(), Mock.Of<ITenantProvider>(), Mock.Of<IAccessControlProvider>(),
        Mock.Of<IEventBus>(), NullLogger<WorkspaceService>.Instance, new AuthMetrics());

    private static AuthorizationFilterContext FilterContext(string? sent, string? configured, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [InternalApiKeyAttribute.ConfigKey] = configured })
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environment))
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        if (sent is not null)
        {
            http.Request.Headers[InternalRoutes.ApiKeyHeader] = sent;
        }

        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
    }
}
