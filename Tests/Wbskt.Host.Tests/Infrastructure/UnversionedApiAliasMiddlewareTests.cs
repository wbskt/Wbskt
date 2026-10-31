using Microsoft.AspNetCore.Http;
using Wbskt.Infrastructure.Middlewares;

namespace Wbskt.Host.Tests.Infrastructure;

public sealed class UnversionedApiAliasMiddlewareTests
{
    [Theory]
    [InlineData("/api/workspaces", "/api/v1/workspaces")]
    [InlineData("/api/workspaces/3fa85f64-5717-4562-b3fc-2c963f66afa6/clients", "/api/v1/workspaces/3fa85f64-5717-4562-b3fc-2c963f66afa6/clients")]
    [InlineData("/api/client-auth/login", "/api/v1/client-auth/login")]
    [InlineData("/api/tenants/", "/api/v1/tenants/")]
    public void An_unversioned_public_path_is_served_as_v1(string path, string expected)
    {
        Assert.True(UnversionedApiAliasMiddleware.TryAlias(path, out var versioned));
        Assert.Equal(expected, versioned.Value);
    }

    [Theory]
    [InlineData("/api/v1/workspaces")]
    [InlineData("/api/v1")]
    [InlineData("/api/internal/workspaces/resolve")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/callbacks/webhook/3fa85f64-5717-4562-b3fc-2c963f66afa6/ttn")]
    [InlineData("/api/inbound/manual")]
    [InlineData("/api")]
    [InlineData("/api/")]
    [InlineData("/hubs/notifications")]
    [InlineData("/healthz")]
    [InlineData("/apifoo/workspaces")]
    public void Other_paths_are_left_alone(string path)
    {
        Assert.False(UnversionedApiAliasMiddleware.TryAlias(path, out var versioned));
        Assert.Equal(path, versioned.Value);
    }

    [Fact]
    public async Task An_aliased_request_is_marked_deprecated()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/workspaces";
        string? seen = null;

        await new UnversionedApiAliasMiddleware(c =>
        {
            seen = c.Request.Path.Value;
            return Task.CompletedTask;
        }).InvokeAsync(context);

        Assert.Equal("/api/v1/workspaces", seen);
        Assert.Equal("true", context.Response.Headers["Deprecation"].ToString());
    }
}
