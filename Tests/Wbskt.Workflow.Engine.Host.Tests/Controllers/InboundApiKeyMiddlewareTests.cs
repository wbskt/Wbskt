using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Engine.Host.Middleware;

namespace Wbskt.Workflow.Engine.Host.Tests.Controllers;

/// <summary>
/// The engine's API key is required on every endpoint except the anonymous health probes, whether or
/// not the endpoint is marked [InboundEndpoint].
/// </summary>
public sealed class InboundApiKeyMiddlewareTests
{
    private const string Key = "engine-key";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_endpoint_without_the_key_is_refused_whether_or_not_it_is_marked_inbound(bool markedInbound)
    {
        var (status, reached) = await InvokeAsync(markedInbound ? Endpoint(new InboundEndpointAttribute()) : Endpoint(), apiKey: null);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.False(reached);
    }

    [Fact]
    public async Task A_request_that_matches_no_endpoint_still_needs_the_key()
    {
        var (status, reached) = await InvokeAsync(endpoint: null, apiKey: "wrong");

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.False(reached);
    }

    [Fact]
    public async Task The_right_key_is_let_through()
    {
        var (_, reached) = await InvokeAsync(Endpoint(), apiKey: Key);

        Assert.True(reached);
    }

    [Fact]
    public async Task An_anonymous_endpoint_needs_no_key()
    {
        var (_, reached) = await InvokeAsync(Endpoint(new AllowAnonymousAttribute()), apiKey: null);

        Assert.True(reached);
    }

    [Fact]
    public async Task Production_without_a_configured_key_refuses_everything_but_anonymous_endpoints()
    {
        var (status, reached) = await InvokeAsync(Endpoint(), apiKey: Key, configuredKey: null);
        var (_, anonymousReached) = await InvokeAsync(Endpoint(new AllowAnonymousAttribute()), apiKey: null, configuredKey: null);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.False(reached);
        Assert.True(anonymousReached);
    }

    private static Endpoint Endpoint(params object[] metadata) => new(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test");

    private static async Task<(int Status, bool Reached)> InvokeAsync(Endpoint? endpoint, string? apiKey, string? configuredKey = Key)
    {
        bool reached = false;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Engine:InboundApiKey"] = configuredKey })
            .Build();
        var environment = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Production);
        var middleware = new InboundApiKeyMiddleware(_ => { reached = true; return Task.CompletedTask; }, configuration, environment);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(endpoint);
        if (apiKey is not null)
        {
            context.Request.Headers[EngineInboundApi.ApiKeyHeader] = apiKey;
        }

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, reached);
    }
}
