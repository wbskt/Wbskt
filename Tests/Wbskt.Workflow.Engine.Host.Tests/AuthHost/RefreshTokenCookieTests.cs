using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Auth.Host.Controllers;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// The rules for when the refresh cookie may be read: only by a request that asked for the cookie
/// transport, and only from an allowed origin, so another site cannot make a browser spend it.
/// </summary>
public sealed class RefreshTokenCookieTests
{
    private const string Console = "https://console.example.test";

    [Fact]
    public void The_cookie_is_read_with_the_header_from_an_allowed_origin()
    {
        var cookie = Cookie();

        Assert.Equal("token", cookie.Read(Request(header: true, origin: Console)));
    }

    [Fact]
    public void The_cookie_is_not_read_without_the_header()
    {
        Assert.Null(Cookie().Read(Request(header: false, origin: Console)));
    }

    [Fact]
    public void The_cookie_is_not_read_from_another_origin()
    {
        Assert.Null(Cookie().Read(Request(header: true, origin: "https://evil.example.test")));
    }

    [Fact]
    public void A_request_without_an_origin_is_not_a_cross_site_one()
    {
        Assert.Equal("token", Cookie().Read(Request(header: true, origin: null)));
    }

    [Fact]
    public void Development_without_configured_origins_allows_any()
    {
        var cookie = Cookie(origins: [], development: true);

        Assert.Equal("token", cookie.Read(Request(header: true, origin: "http://localhost:4200")));
    }

    [Fact]
    public void Production_without_configured_origins_allows_none()
    {
        var cookie = Cookie(origins: [], development: false);

        Assert.Null(cookie.Read(Request(header: true, origin: "http://localhost:4200")));
    }

    [Fact]
    public void The_written_cookie_is_HttpOnly_Secure_and_scoped_to_the_auth_endpoints()
    {
        var context = new DefaultHttpContext();

        Cookie(sameSite: SameSiteMode.None).Write(context.Response, "token");

        var setCookie = context.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        Assert.StartsWith("wbskt_refresh=token", setCookie);
        Assert.Contains("httponly", setCookie);
        Assert.Contains("secure", setCookie);
        Assert.Contains("samesite=none", setCookie);
        Assert.Contains("path=/api/auth", setCookie);
        Assert.Contains("expires=", setCookie);
    }

    private static RefreshTokenCookie Cookie(string[]? origins = null, bool development = false, SameSiteMode sameSite = SameSiteMode.Strict)
    {
        var settings = new Dictionary<string, string?>();
        var list = origins ?? [Console];
        for (var i = 0; i < list.Length; i++)
        {
            settings[$"Cors:AllowedOrigins:{i}"] = list[i];
        }

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(development ? Environments.Development : Environments.Production);

        return new RefreshTokenCookie(
            Options.Create(new RefreshCookieOptions { SameSite = sameSite }),
            Options.Create(new AccessTokenOptions()),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            environment.Object);
    }

    private static HttpRequest Request(bool header, string? origin)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "wbskt_refresh=token";
        if (header)
        {
            context.Request.Headers[RefreshTokenCookie.TransportHeader] = RefreshTokenCookie.CookieTransport;
        }

        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        return context.Request;
    }
}
