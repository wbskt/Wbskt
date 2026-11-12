using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Infrastructure.Middlewares;

namespace Wbskt.Host.Tests.Infrastructure;

public sealed class InternalRouteGuardMiddlewareTests
{
    [Theory]
    [InlineData("/api/internal/workspaces/resolve", "X-Forwarded-For")]
    [InlineData("/API/Internal/workspaces/resolve", "X-Forwarded-For")]
    [InlineData("/api/internal", "X-Forwarded-Host")]
    [InlineData("/Api/INTERNAL/users/me", "X-Forwarded-Host")]
    public void A_forwarded_request_for_an_internal_route_is_refused(string path, string header)
    {
        Assert.True(InternalRouteGuardMiddleware.IsProxiedInternalRequest(Context(path, header)));
    }

    [Theory]
    [InlineData("/api/internal/workspaces/resolve", null)]
    [InlineData("/API/Internal/workspaces/resolve", null)]
    [InlineData("/api/v1/workspaces", "X-Forwarded-For")]
    [InlineData("/api/internals", "X-Forwarded-For")]
    [InlineData("/api/v1/internal", "X-Forwarded-For")]
    public void Other_requests_pass(string path, string? header)
    {
        Assert.False(InternalRouteGuardMiddleware.IsProxiedInternalRequest(Context(path, header)));
    }

    /// <summary>
    /// A rewrite before the guard can change the path (the unversioned alias turns /api/Internal into
    /// /api/v1/Internal), so what routing picked decides as well.
    /// </summary>
    [Fact]
    public void A_forwarded_request_routed_to_an_internal_endpoint_is_refused_whatever_its_path()
    {
        var context = Context("/api/v1/Internal/users/me", "X-Forwarded-For");
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("api/internal/users/me"), 0, null, null));

        Assert.True(InternalRouteGuardMiddleware.IsProxiedInternalRequest(context));
    }

    [Fact]
    public async Task A_refused_request_is_a_404_and_goes_no_further()
    {
        var context = Context("/Api/Internal/users/me", "X-Forwarded-For");
        bool reachedNext = false;

        await new InternalRouteGuardMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        }, NullLogger<InternalRouteGuardMiddleware>.Instance).InvokeAsync(context);

        Assert.False(reachedNext);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    private static DefaultHttpContext Context(string path, string? header)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (header is not null)
        {
            context.Request.Headers[header] = "203.0.113.9";
        }

        return context;
    }
}
