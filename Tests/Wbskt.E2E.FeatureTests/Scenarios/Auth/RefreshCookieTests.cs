using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 5a of Docs/Auth.Host.E2E.Scenarios.md — the refresh token as an HttpOnly cookie, which a
/// browser client asks for with <c>X-Refresh-Token-Transport: cookie</c>.
///
/// These keep their own client with cookie handling off, so each test decides exactly which cookie
/// goes on which request and can read every Set-Cookie the host sends back.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class RefreshCookieTests(ServicesFixture fixture) : IDisposable
{
    private const string CookieName = "wbskt_refresh";
    private const string TransportHeader = "X-Refresh-Token-Transport";

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        UseCookies = false,
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    }) { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose() => _http.Dispose();

    [SkippableFact]
    public async Task AUTH_RC_01_Login_SetsAnHttpOnlyCookie_AndLeavesTheTokenOutOfTheBody()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var user = await fixture.CreateUserAsync();

        using var response = await LoginAsync(user);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.TryGetProperty("refreshToken", out _).Should().BeFalse("the page must never see the refresh token");

        var setCookie = RefreshSetCookie(response);
        setCookie.Should().NotBeNull();
        setCookie!.ToLowerInvariant().Should()
            .Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/api/auth");
    }

    [SkippableFact]
    public async Task AUTH_RC_02_Refresh_FromTheCookie_RotatesIt()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var user = await fixture.CreateUserAsync();
        string first = CookieValue(await LoginAsync(user));

        using var refreshed = await PostWithCookieAsync("/api/auth/refresh-token", first);

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.TryGetProperty("refreshToken", out _).Should().BeFalse();
        CookieValue(refreshed).Should().NotBe(first, "every refresh issues a new token");
    }

    [SkippableFact]
    public async Task AUTH_RC_03_A_refused_cookie_is_cleared()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var user = await fixture.CreateUserAsync();
        string first = CookieValue(await LoginAsync(user));
        (await PostWithCookieAsync("/api/auth/refresh-token", first)).EnsureSuccessStatusCode();

        // The retired token again: a replay, refused, and the browser is told to drop it.
        using var replay = await PostWithCookieAsync("/api/auth/refresh-token", first);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        IsCleared(replay).Should().BeTrue();
    }

    [SkippableFact]
    public async Task AUTH_RC_04_The_cookie_is_ignored_without_the_header()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var user = await fixture.CreateUserAsync();
        string cookie = CookieValue(await LoginAsync(user));

        using var request = new HttpRequestMessage(HttpMethod.Post, ServicesFixture.AuthUrl("/api/auth/refresh-token"))
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        using var response = await _http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a request that did not ask for the cookie transport is a body-only request, and has no token");
    }

    [SkippableFact]
    public async Task AUTH_RC_05_Logout_EndsTheCookiesSession_AndClearsIt()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var user = await fixture.CreateUserAsync();
        string cookie = CookieValue(await LoginAsync(user));

        using var logout = await PostWithCookieAsync("/api/auth/logout", cookie);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        IsCleared(logout).Should().BeTrue();
        (await PostWithCookieAsync("/api/auth/refresh-token", cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_RC_06_Refresh_WithTheHeaderButNoCookie_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        using var response = await PostWithCookieAsync("/api/auth/refresh-token", cookie: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a browser whose cookie expired is signed out, the same answer as an expired token");
    }

    private async Task<HttpResponseMessage> LoginAsync(ServicesFixture.TestUser user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ServicesFixture.AuthUrl("/api/auth/login"))
        {
            Content = JsonContent.Create(new { email = user.Email, password = user.Password })
        };
        request.Headers.Add(TransportHeader, "cookie");
        return await _http.SendAsync(request);
    }

    /// <summary>A body-less POST in the cookie transport, as the console sends refresh and logout.</summary>
    private async Task<HttpResponseMessage> PostWithCookieAsync(string path, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ServicesFixture.AuthUrl(path));
        request.Headers.Add(TransportHeader, "cookie");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        }

        return await _http.SendAsync(request);
    }

    private static string? RefreshSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith($"{CookieName}=", StringComparison.Ordinal))
            : null;

    private static string CookieValue(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var setCookie = RefreshSetCookie(response) ?? throw new InvalidOperationException("No refresh cookie was set.");
        return setCookie[(CookieName.Length + 1)..].Split(';')[0];
    }

    private static bool IsCleared(HttpResponseMessage response)
    {
        var setCookie = RefreshSetCookie(response);
        return setCookie is not null
            && setCookie[(CookieName.Length + 1)..].Split(';')[0].Length == 0
            && setCookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
    }
}
