using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.RateLimiting;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// The export and integration-test budgets are per workspace, but spent in an action filter, after
/// authentication and the permission check, so an outsider who knows a workspace's reference cannot
/// use them up (#1081).
/// </summary>
public sealed class WorkspaceRateLimitFilterTests
{
    [Fact]
    public async Task A_workspace_past_its_budget_is_refused_with_429_and_another_is_not()
    {
        using var limiter = Limiter(permitLimit: 1);
        var filter = new WorkspaceRateLimitFilter(limiter);
        Guid workspace = Guid.NewGuid();

        var first = await RunAsync(filter, workspace.ToString());
        var second = await RunAsync(filter, workspace.ToString("N").ToUpperInvariant());
        var other = await RunAsync(filter, Guid.NewGuid().ToString());

        Assert.True(first.Ran);
        Assert.False(second.Ran);
        var problem = Assert.IsType<ApiErrorResults.ProblemResult>(second.Context.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.StatusCode);
        Assert.Equal("RATE_LIMITED", problem.Problem.Extensions["code"]?.ToString());
        Assert.True(second.Context.HttpContext.Response.Headers.ContainsKey("Retry-After"));
        Assert.True(other.Ran);
    }

    [Fact]
    public async Task An_action_without_a_workspace_limit_spends_nothing()
    {
        using var limiter = Limiter(permitLimit: 1);
        var filter = new WorkspaceRateLimitFilter(limiter);
        Guid workspace = Guid.NewGuid();

        Assert.True((await RunAsync(filter, workspace.ToString(), limited: false)).Ran);
        Assert.True((await RunAsync(filter, workspace.ToString(), limited: false)).Ran);
        Assert.True((await RunAsync(filter, workspace.ToString())).Ran);
    }

    private static WorkspaceRateLimiter Limiter(int permitLimit) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Export:PermitLimit"] = permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            .Build());

    private static async Task<(bool Ran, ActionExecutingContext Context)> RunAsync(WorkspaceRateLimitFilter filter, string workspaceRef, bool limited = true)
    {
        var routeData = new RouteData();
        routeData.Values["workspaceRef"] = workspaceRef;
        var descriptor = new ActionDescriptor { EndpointMetadata = limited ? [new WorkspaceRateLimitAttribute(RateLimitPolicies.Export)] : [] };
        var actionContext = new ActionContext(new DefaultHttpContext(), routeData, descriptor);
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());

        bool ran = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        });

        return (ran, context);
    }
}
