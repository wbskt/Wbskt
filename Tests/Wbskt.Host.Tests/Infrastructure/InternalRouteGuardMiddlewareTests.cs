using Microsoft.AspNetCore.Http;
using Wbskt.Infrastructure.Middlewares;

namespace Wbskt.Host.Tests.Infrastructure;

public sealed class InternalRouteGuardMiddlewareTests
{
    [Theory]
    [InlineData("/api/internal/workspaces/resolve", "X-Forwarded-For")]
    [InlineData("/API/Internal/workspaces/resolve", "X-Forwarded-For")]
    [InlineData("/api/internal", "X-Forwarded-Host")]
    public void A_forwarded_request_for_an_internal_route_is_refused(string path, string header)
    {
        Assert.True(InternalRouteGuardMiddleware.IsProxiedInternalRequest(Request(path, header)));
    }

    [Theory]
    [InlineData("/api/internal/workspaces/resolve", null)]
    [InlineData("/api/v1/workspaces", "X-Forwarded-For")]
    [InlineData("/api/internals", "X-Forwarded-For")]
    [InlineData("/api/v1/internal", "X-Forwarded-For")]
    public void Other_requests_pass(string path, string? header)
    {
        Assert.False(InternalRouteGuardMiddleware.IsProxiedInternalRequest(Request(path, header)));
    }

    [Fact]
    public async Task A_refused_request_is_a_404_and_goes_no_further()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/Api/Internal/users/me";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        bool reachedNext = false;

        await new InternalRouteGuardMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        }).InvokeAsync(context);

        Assert.False(reachedNext);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    private static HttpRequest Request(string path, string? header)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (header is not null)
        {
            context.Request.Headers[header] = "203.0.113.9";
        }

        return context.Request;
    }
}
