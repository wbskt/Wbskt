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
/// fail with a 429 for the rest of the window, and that includes the next scenario in this class.
/// Set <c>E2E_RATE_LIMIT_TESTS=1</c> to enable, and run them one at a time against an auth host
/// freshly restarted with the production limit (the Development settings raise it; see the README):
/// </para>
/// <code>
/// RateLimiting__Authentication__PermitLimit=10 Tests/Wbskt.E2E.FeatureTests/start-hosts.sh auth
/// E2E_RATE_LIMIT_TESTS=1 E2E_AUTH_PERMIT_LIMIT=10 dotnet test --filter FullyQualifiedName~RateLimitingTests.AUTH_RL_01
/// </code>
/// <para>
/// The limit is a brake on bulk attempts from one address — the point is that the password hasher
/// cannot be used as a work amplifier. Guessing one account's password from many addresses is what
/// the per-account lockout (AccountSecurityTests) is for.
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
    private async Task<IReadOnlyList<HttpStatusCode>> ExhaustAsync(Func<Task<HttpResponseMessage>> call, int? permitLimit = null)
    {
        var statuses = new List<HttpStatusCode>();

        // A couple past the limit, so the transition is unambiguous rather than borderline.
        for (var i = 0; i < (permitLimit ?? E2EConfig.AuthPermitLimit) + 3; i++)
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

        // Exhausted with an address that has no account: wrong passwords against the user's own
        // address would also trip the per-account lockout, which outlasts this window.
        await ExhaustAsync(() => fixture.LoginRawAsync($"nobody-{Guid.NewGuid():N}@test.local", "wrong-password"));

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

        // Its own, larger bucket: with access tokens lasting minutes, refreshing is routine traffic.
        var statuses = await ExhaustAsync(() => fixture.RefreshAsync($"bogus-{Guid.NewGuid():N}"), E2EConfig.RefreshPermitLimit);

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    [SkippableFact]
    public async Task AUTH_RL_05_LogoutIsRateLimited()
    {
        SkipUnlessEnabled();

        // In the refresh bucket, not the credential one: see AUTH_RL_11.
        var statuses = await ExhaustAsync(() => fixture.LogoutAsync($"bogus-{Guid.NewGuid():N}"), E2EConfig.RefreshPermitLimit);

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

    [SkippableFact]
    public async Task AUTH_RL_11_SigningOutDoesNotSpendTheSignInBudget()
    {
        SkipUnlessEnabled();

        var user = await fixture.CreateUserAsync();

        // More logouts than the credential bucket holds. On a shared office address, people signing
        // out must not lock their colleagues out of signing in.
        var statuses = await ExhaustAsync(() => fixture.LogoutAsync($"bogus-{Guid.NewGuid():N}"));
        statuses.Should().NotContain(HttpStatusCode.TooManyRequests);

        var response = await fixture.LoginRawAsync(user.Email, user.Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_RL_12_VerificationLinksHaveTheirOwnBucket()
    {
        SkipUnlessEnabled();

        var user = await fixture.CreateUserAsync();

        var statuses = await ExhaustAsync(
            () => fixture.SendAsync(HttpMethod.Post, ServicesFixture.AuthUrl("/api/auth/verify-email"), body: new { Token = $"bogus-{Guid.NewGuid():N}" }),
            E2EConfig.VerificationPermitLimit);
        statuses.Should().Contain(HttpStatusCode.TooManyRequests);

        // Exhausting it leaves signing in alone.
        var response = await fixture.LoginRawAsync(user.Email, user.Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
    public async Task AUTH_RL_08_MetricsAreNotServedAnonymously()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(HttpMethod.Get, ServicesFixture.AuthUrl("/metrics"));

        // This host is publicly routed, so a scrape endpoint here would be world-readable.
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_RL_09_ThereIsNoScrapeEndpointEvenWithAToken()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl("/metrics"), user.Token);

        // Metrics leave by OTLP push to the collector on the backend network; nothing is scraped
        // from a host, so there is nothing to expose on a public router.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
