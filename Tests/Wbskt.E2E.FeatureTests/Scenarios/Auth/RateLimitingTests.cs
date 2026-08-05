using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 7 of Docs/Auth.Host.E2E.Scenarios.md — the credential endpoints' rate limit, and the two
/// operational endpoints.
///
/// <para>
/// <b>These are opt-in and meant to be run alone.</b> The limiter partitions on the caller's IP,
/// which every test in this suite shares, so exhausting the budget here makes unrelated scenarios
/// fail with a 429 for the rest of the window. Set <c>E2E_RATE_LIMIT_TESTS=1</c> to enable, and
/// prefer running them with a filter rather than alongside the rest:
/// </para>
/// <code>
/// E2E_RATE_LIMIT_TESTS=1 dotnet test --filter FullyQualifiedName~RateLimitingTests
/// </code>
/// <para>
/// The limit is a brake on bulk attempts rather than per-account lockout — an unauthenticated
/// endpoint has no better partition key available, and the point is that the password hasher cannot
/// be used as a work amplifier.
/// </para>
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class RateLimitingTests(ServicesFixture fixture)
{
    private void SkipUnlessEnabled()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        Skip.IfNot(E2EConfig.RateLimitTestsEnabled,
            "Rate-limit scenarios exhaust the shared per-IP budget. Set E2E_RATE_LIMIT_TESTS=1 to run them.");
    }

    /// <summary>Hammers an endpoint past the permit limit and reports the statuses seen.</summary>
    private async Task<IReadOnlyList<HttpStatusCode>> ExhaustAsync(Func<Task<HttpResponseMessage>> call)
    {
        var statuses = new List<HttpStatusCode>();

        // A couple past the limit, so the transition is unambiguous rather than borderline.
        for (var i = 0; i < E2EConfig.AuthPermitLimit + 3; i++)
        {
            statuses.Add((await call()).StatusCode);
        }

        return statuses;
    }

    [SkippableFact]
    public async Task AUTH_RL_01_FailedLoginsPastTheLimit_Return429()
    {
        SkipUnlessEnabled();

        var statuses = await ExhaustAsync(() =>
            fixture.LoginRawAsync($"nobody-{Guid.NewGuid():N}@test.local", "wrong-password"));

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "the limiter is what stops the password hasher being used as a work amplifier");
    }

    [SkippableFact]
    public async Task AUTH_RL_02_AfterTheWindowElapses_LoginWorksAgain()
    {
        SkipUnlessEnabled();

        var user = await fixture.CreateUserAsync();
        await ExhaustAsync(() => fixture.LoginRawAsync(user.Email, "wrong-password"));

        // A fixed window, so waiting it out is the whole recovery path. Slow by nature — this is
        // the test that makes the suite worth isolating.
        await Task.Delay(TimeSpan.FromMinutes(E2EConfig.AuthWindowMinutes) + TimeSpan.FromSeconds(5));

        var response = await fixture.LoginRawAsync(user.Email, user.Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_RL_03_RegisterIsRateLimited()
    {
        SkipUnlessEnabled();

        var statuses = await ExhaustAsync(() =>
        {
            var (username, email, password) = ServicesFixture.NewCredentials();
            return fixture.RegisterAsync(username, email, password);
        });

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    [SkippableFact]
    public async Task AUTH_RL_04_RefreshIsRateLimited()
    {
        SkipUnlessEnabled();

        var statuses = await ExhaustAsync(() => fixture.RefreshAsync($"bogus-{Guid.NewGuid():N}"));

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    [SkippableFact]
    public async Task AUTH_RL_05_LogoutIsRateLimited()
    {
        SkipUnlessEnabled();

        var statuses = await ExhaustAsync(() => fixture.LogoutAsync($"bogus-{Guid.NewGuid():N}"));

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    [SkippableFact]
    public async Task AUTH_RL_06_LogoutAllIsNotRateLimited()
    {
        SkipUnlessEnabled();

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var statuses = await ExhaustAsync(() => fixture.LogoutAllAsync(session.AccessToken));

        // No [EnableRateLimiting] on this action — it is authenticated, so it is not the bulk-attempt
        // surface the policy exists for. Pinned so removing or adding the attribute is deliberate.
        statuses.Should().NotContain(HttpStatusCode.TooManyRequests);
    }

    [SkippableFact]
    public async Task AUTH_RL_10_SuccessfulLoginsAlsoConsumeTheBudget()
    {
        SkipUnlessEnabled();

        var user = await fixture.CreateUserAsync();

        var statuses = await ExhaustAsync(() => fixture.LoginRawAsync(user.Email, user.Password));

        // The window counts requests, not failures. Otherwise the brake could be walked around by
        // interleaving valid credentials between guesses.
        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    // ── Operational endpoints (not rate limited, but part of §7) ──────────────────────────

    [SkippableFact]
    public async Task AUTH_RL_07_HealthzIsAnonymous()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(HttpMethod.Get, ServicesFixture.AuthUrl("/healthz"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_RL_08_MetricsRequiresAuthorization()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(HttpMethod.Get, ServicesFixture.AuthUrl("/metrics"));

        // This host is publicly routed, so an anonymous scrape endpoint would be world-readable.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_RL_09_MetricsIsReadableWithAToken()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl("/metrics"), user.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();
    }
}
