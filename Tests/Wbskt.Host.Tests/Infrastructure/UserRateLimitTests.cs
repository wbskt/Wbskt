using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// The signed-in API's budget, through the shared host pipeline on a real Kestrel listener: it is per
/// user, so one caller over it is refused with 429 and Retry-After while another is not, and device
/// tokens and anonymous calls do not spend it.
/// </summary>
public sealed class UserRateLimitTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Grace = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Device = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task A_user_over_the_budget_is_refused_while_another_is_not()
    {
        await using var app = await StartAsync(burst: 3);
        using var client = new HttpClient { BaseAddress = BaseAddress(app) };

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, "user", Ada)).StatusCode);
        }

        using var refused = await SendAsync(client, "user", Ada);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(int.Parse(refused.Headers.GetValues("Retry-After").Single()) >= 1);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("RATE_LIMITED", body.RootElement.GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, "user", Grace)).StatusCode);
    }

    [Fact]
    public async Task Devices_and_anonymous_callers_do_not_spend_a_budget()
    {
        await using var app = await StartAsync(burst: 1);
        using var client = new HttpClient { BaseAddress = BaseAddress(app) };

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, "client", Device)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, null, Guid.Empty)).StatusCode);
        }
    }

    [Theory]
    [InlineData(600, 10, 1)]
    [InlineData(30, 1, 2)]
    [InlineData(90, 3, 2)]
    [InlineData(45, 3, 4)]
    [InlineData(7, 7, 60)]
    [InlineData(1, 1, 60)]
    [InlineData(100000, 5000, 3)]
    [InlineData(0, 1, 60)]
    public void The_refill_matches_the_configured_rate_exactly(int permitsPerMinute, int tokens, int seconds)
    {
        var (perPeriod, period) = UserRateLimiter.Refill(permitsPerMinute);

        Assert.Equal(tokens, perPeriod);
        Assert.Equal(TimeSpan.FromSeconds(seconds), period);
    }

    private static async Task<WebApplication> StartAsync(int burst)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // A refill of one a minute, so the test's few requests cannot earn one back.
            ["RateLimiting:Api:PermitsPerMinute"] = "1",
            ["RateLimiting:Api:Burst"] = burst.ToString(),
        });
        builder.Services.AddWbsktProblemDetails();
        builder.Services.AddUserRateLimit(builder.Configuration);
        // GlobalExceptionMiddleware reports faults on the bus.
        builder.Services.AddSingleton(Mock.Of<IEventBus>());
        builder.Services.AddAuthentication(HeaderAuthentication.Scheme)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(HeaderAuthentication.Scheme, null);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseWbsktPipeline();
        app.MapGet("/ping", () => "pong");
        await app.StartAsync();
        return app;
    }

    private static Uri BaseAddress(WebApplication app) =>
        new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string? type, Guid subject)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/ping");
        if (type is not null)
        {
            request.Headers.Add(HeaderAuthentication.TypeHeader, type);
            request.Headers.Add(HeaderAuthentication.SubjectHeader, subject.ToString());
        }

        return client.SendAsync(request);
    }

    /// <summary>Signs a request in as whoever its test headers name, with the claims a real token carries.</summary>
    private sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "test";
        public const string TypeHeader = "X-Test-Type";
        public const string SubjectHeader = "X-Test-Subject";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(TypeHeader, out var type) || !Guid.TryParse(Request.Headers[SubjectHeader], out Guid subject))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            Claim[] claims = type == WbsktClaims.UserType
                ? WbsktClaims.ForUser(subject, "someone", "someone@example.test", Guid.NewGuid())
                : ClientClaims.For(subject, "device", Guid.NewGuid(), 7, 3);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
