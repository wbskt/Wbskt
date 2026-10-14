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
        app.UseWbsktPipeline();
        app.MapGet("/ip", (HttpContext context) => context.Connection.RemoteIpAddress!.ToString()).RequireRateLimiting(Policy);
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
