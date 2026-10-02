using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 6 of Docs/Auth.Host.E2E.Scenarios.md — <c>POST /api/auth/logout</c> and
/// <c>/logout-all</c>.
///
/// Logout is anonymous by necessity: the caller may have already discarded their access token, and
/// the refresh token is itself the credential for the session being ended. That forces the endpoint
/// to answer identically whether or not the token existed — a distinguishable response would make
/// it a way to test whether a stolen token string is live. AUTH_OUT_02 is that property.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class SessionTerminationTests(ServicesFixture fixture)
{
    // ── Single session ────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_OUT_01_Logout_EndsThatSession()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        (await fixture.LogoutAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.RefreshAsync(session.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_OUT_02_LogoutOfAnUnknownToken_IsIndistinguishable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var real = await fixture.LogoutAsync(session.RefreshToken);
        var fictional = await fixture.LogoutAsync($"no-such-token-{Guid.NewGuid():N}");

        // Status *and* body must match, or the endpoint becomes an oracle for testing whether a
        // token string is live — which is exactly what someone holding a stolen one wants to know.
        fictional.StatusCode.Should().Be(real.StatusCode);
        (await fictional.Content.ReadAsStringAsync())
            .Should().Be(await real.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task AUTH_OUT_03_LoggingOutTwice_IsIdempotent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        await fixture.LogoutAsync(session.RefreshToken);
        var second = await fixture.LogoutAsync(session.RefreshToken);

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task AUTH_OUT_04_LogoutIsAnonymous()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        // No Authorization header — a client that has already dropped its access token still has to
        // be able to end its session.
        var response = await fixture.LogoutAsync(session.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task AUTH_OUT_05_LogoutEndsOnlyThePresentedSession()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var deviceOne = await fixture.LoginAsync(user.Email, user.Password);
        var deviceTwo = await fixture.LoginAsync(user.Email, user.Password);

        await fixture.LogoutAsync(deviceOne.RefreshToken);

        (await fixture.RefreshAsync(deviceTwo.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.OK, "signing out of one device must not sign out the others");
    }

    [SkippableFact]
    public async Task AUTH_OUT_06_MissingRefreshToken_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(
            HttpMethod.Post, ServicesFixture.AuthUrl("/api/auth/logout"), body: new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Every session ─────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_OUT_07_LogoutAll_EndsEverySession()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var deviceOne = await fixture.LoginAsync(user.Email, user.Password);
        var deviceTwo = await fixture.LoginAsync(user.Email, user.Password);
        var deviceThree = await fixture.LoginAsync(user.Email, user.Password);

        (await fixture.LogoutAllAsync(deviceOne.AccessToken)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        foreach (var session in new[] { deviceOne, deviceTwo, deviceThree })
        {
            (await fixture.RefreshAsync(session.RefreshToken)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [SkippableFact]
    public async Task AUTH_OUT_08_LogoutAllWithoutAToken_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(
            HttpMethod.Post, ServicesFixture.AuthUrl("/api/auth/logout-all"));

        // Unlike logout, this one needs to know *whose* sessions to end, so it cannot be anonymous.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_OUT_09_LogoutAllWithAGarbageToken_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(
            HttpMethod.Post, ServicesFixture.AuthUrl("/api/auth/logout-all"), "not-a-jwt");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_OUT_10_LogoutAllDoesNotTouchOtherAccounts()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var alice = await fixture.CreateUserAsync();
        var aliceSession = await fixture.LoginAsync(alice.Email, alice.Password);

        var bob = await fixture.CreateUserAsync();
        var bobSession = await fixture.LoginAsync(bob.Email, bob.Password);

        await fixture.LogoutAllAsync(aliceSession.AccessToken);

        (await fixture.RefreshAsync(bobSession.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_OUT_11_LogoutAllEndsTheAccessTokensToo()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);
        var other = await fixture.LoginAsync(user.Email, user.Password);

        // iat has second resolution and a token from the revocation's own second is let through
        // (see AccessTokenRevocation), so the tokens must be at least a second old to be revoked.
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        await fixture.LogoutAllAsync(session.AccessToken);

        // "Sign out everywhere" is a containment measure only if it reaches the access tokens
        // already handed out - including the one that asked, and one from another device.
        foreach (var token in new[] { session.AccessToken, other.AccessToken })
        {
            var response = await fixture.SendAsync(
                HttpMethod.Get, ServicesFixture.AuthUrl("/api/workspaces"), token);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [SkippableFact]
    public async Task AUTH_OUT_12_LogoutAllIsIdempotent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        await fixture.LogoutAllAsync(session.AccessToken);

        // A fresh session, because logout-all also revokes the access token that called it.
        var again = await fixture.LoginAsync(user.Email, user.Password);
        var second = await fixture.LogoutAllAsync(again.AccessToken);

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
