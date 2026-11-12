using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Infrastructure.Middlewares;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// The shared host pipeline, on a real Kestrel listener: the forwarded headers are applied before the
/// rate limiter partitions on the client address, and CORS answers with the configured origins.
/// </summary>
public sealed class WbsktHostPipelineTests
{
    private const string Policy = "test-per-ip";

    [Fact]
    public async Task The_client_address_comes_from_a_known_proxy_and_the_limiter_partitions_on_it()
    {
        await using var app = await StartAsync();
        using var client = new HttpClient { BaseAddress = BaseAddress(app) };

        Assert.Equal("203.0.113.9", await GetIpAsync(client, "203.0.113.9"));

        // One permit per address: the same client is refused, another is not.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync(client, "203.0.113.9")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, "198.51.100.7")).StatusCode);
    }

    [Fact]
    public async Task An_allowed_origin_gets_cors_headers_with_the_exposed_ones()
    {
        await using var app = await StartAsync();
        using var client = new HttpClient { BaseAddress = BaseAddress(app) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ip");
        request.Headers.Add("Origin", "https://console.example.test");
        request.Headers.Add("X-Forwarded-For", "192.0.2.1");

        using var response = await client.SendAsync(request);

        Assert.Equal("https://console.example.test", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("X-Total-Count", response.Headers.GetValues("Access-Control-Expose-Headers").Single());
    }

    /// <summary>
    /// #775: the guard sees the forwarded header before the forwarded-headers step, which would
    /// consume it from this known proxy, and the route still answers a direct call.
    /// </summary>
    [Fact]
    public async Task An_internal_route_answers_a_direct_call_but_not_a_forwarded_one()
    {
        await using var app = await StartAsync();
        using var client = new HttpClient { BaseAddress = BaseAddress(app) };
        using var direct = await client.GetAsync("/api/internal/ping");
        using var directMixedCase = await client.GetAsync("/API/Internal/ping");

        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, directMixedCase.StatusCode);
        foreach (string path in new[] { "/api/internal/ping", "/API/Internal/ping", "/api/Internal/ping" })
        {
            using var forwarded = new HttpRequestMessage(HttpMethod.Get, path);
            forwarded.Headers.Add("X-Forwarded-For", "203.0.113.9");
            using var refused = await client.SendAsync(forwarded);
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "127.0.0.1",
            ["Cors:AllowedOrigins:0"] = "https://console.example.test",
            ["RateLimiting:Test:PermitLimit"] = "1",
        });
        builder.Services.AddWbsktForwardedHeaders(builder.Configuration);
        builder.Services.AddWbsktCors(builder.Configuration, builder.Environment, "X-Total-Count");
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPerIpPolicy(builder.Configuration, Policy, "Test", defaultPermitLimit: 100);
        });
        // GlobalExceptionMiddleware reports faults on the bus.
        builder.Services.AddSingleton(Mock.Of<IEventBus>());
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();

        var app = builder.Build();

        // As the auth and management hosts do: the alias rewrite and routing come first.
        app.UseUnversionedApiAliases();
        app.UseWbsktPipeline();
        app.MapGet("/ip", (HttpContext context) => context.Connection.RemoteIpAddress!.ToString()).RequireRateLimiting(Policy);
        app.MapGet("/api/internal/ping", () => "pong").AllowAnonymous();
        await app.StartAsync();
        return app;
    }

    private static Uri BaseAddress(WebApplication app) =>
        new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());

    private static async Task<string> GetIpAsync(HttpClient client, string forwardedFor)
    {
        using var response = await SendAsync(client, forwardedFor);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/ip");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }
}
