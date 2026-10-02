using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 5 of Docs/Auth.Host.E2E.Scenarios.md — refresh-token rotation and its theft detection.
///
/// The central behaviour: every exchange retires the presented token, and re-presenting a retired
/// one is read as evidence the session family is compromised, so *every* session for that user is
/// revoked. That logic is the reason a stolen refresh token has a bounded blast radius, and nothing
/// exercised it before this file.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class TokenRotationTests(ServicesFixture fixture)
{
    // ── The happy path ────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_RT_01_Refresh_ValidToken_ReturnsNewPair()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var response = await fixture.RefreshAsync(session.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotated = await ServicesFixture.ReadSessionAsync(response);
        rotated.AccessToken.Should().NotBeNullOrWhiteSpace();
        rotated.RefreshToken.Should().NotBeNullOrWhiteSpace();
        rotated.RefreshToken.Should().NotBe(session.RefreshToken,
            "rotation must issue a new refresh token, or the presented one never actually retires");
    }

    [SkippableFact]
    public async Task AUTH_RT_02_RotatedAccessToken_AuthenticatesProtectedCall()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var response = await fixture.RefreshAsync(session.RefreshToken);
        var rotated = await ServicesFixture.ReadSessionAsync(response);

        var protectedCall = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl("/api/workspaces"), rotated.AccessToken);

        protectedCall.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_RT_03_ChainedRotation_SucceedsRepeatedly()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var current = session.RefreshToken;
        for (var i = 0; i < 3; i++)
        {
            var response = await fixture.RefreshAsync(current);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"rotation {i + 1} of the chain must succeed");

            current = (await ServicesFixture.ReadSessionAsync(response)).RefreshToken;
        }
    }

    [SkippableFact]
    public async Task AUTH_RT_16_Refresh_IsAnonymous()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        // No Authorization header is sent — the refresh token is itself the credential.
        var response = await fixture.RefreshAsync(session.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_RT_17_AccessTokenIssuedBeforeRotation_StaysValid()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        await fixture.RefreshAsync(session.RefreshToken);

        // Documents the intended window: rotation retires refresh tokens, never access tokens.
        var protectedCall = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl("/api/workspaces"), session.AccessToken);

        protectedCall.StatusCode.Should().Be(HttpStatusCode.OK,
            "an access token is self-contained and lives until it expires");
    }

    // ── Theft detection ───────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_RT_04_ReplayingRetiredToken_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        await fixture.RefreshAsync(session.RefreshToken);

        var replay = await fixture.RefreshAsync(session.RefreshToken);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ServicesFixture.ReadErrorCodeAsync(replay)).Should().Be("AUTH_TOKEN_INACTIVE");
    }

    [SkippableFact]
    public async Task AUTH_RT_05_ReplayRevokesTheWholeSessionFamily()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var rotatedResponse = await fixture.RefreshAsync(session.RefreshToken);
        var rotated = await ServicesFixture.ReadSessionAsync(rotatedResponse);

        // Replaying the retired token means someone kept a copy. The legitimate client's *current*
        // token has to die too, or a thief simply keeps rotating alongside the real session.
        await fixture.RefreshAsync(session.RefreshToken);

        var survivor = await fixture.RefreshAsync(rotated.RefreshToken);

        survivor.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a replay condemns the family, not just the token that was replayed");
    }

    [SkippableFact]
    public async Task AUTH_RT_06_AfterFamilyRevocation_UserCanLogInAgain()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        await fixture.RefreshAsync(session.RefreshToken);
        await fixture.RefreshAsync(session.RefreshToken);

        // Revocation ends sessions; it must not lock the account.
        var recovered = await fixture.LoginAsync(user.Email, user.Password);

        recovered.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var response = await fixture.RefreshAsync(recovered.RefreshToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_RT_15_ConcurrentRefreshWithSameToken_YieldsExactlyOneSession()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        // One token must never become two live families. RefreshToken_Revoke is an atomic
        // compare-and-swap that reports how many rows it retired, but the rotation mints the
        // replacement before calling it and ignores the count — so both racers can win.
        var responses = await Task.WhenAll(
            fixture.RefreshAsync(session.RefreshToken),
            fixture.RefreshAsync(session.RefreshToken));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1,
            "a refresh token is single-use; two winners defeat the replay detection entirely, "
            + "because the thief's family survives the victim's next rotation");
    }

    // ── Rejections ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_RT_07_Refresh_GarbageToken_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.RefreshAsync($"not-a-real-token-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_INVALID_TOKEN");
    }

    [SkippableFact]
    public async Task AUTH_RT_08_Refresh_EmptyToken_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.RefreshAsync(string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "[Required] rejects an empty token before the service ever looks it up");
    }

    [SkippableFact]
    public async Task AUTH_RT_10_Refresh_WithAccessToken_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        // The two token types are not interchangeable, even though both are opaque to the caller.
        // An access token is a JWT, longer than RefreshTokenRequest's 255-character bound, so it is
        // turned away by validation before the service looks it up, and never yields a new pair.
        var response = await fixture.RefreshAsync(session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_RT_09_Refresh_BindsToTheTokenOwnerNotTheCaller()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var userA = await fixture.CreateUserAsync();
        var sessionA = await fixture.LoginAsync(userA.Email, userA.Password);
        var workspaceA = await fixture.CreateWorkspaceAsync(sessionA.AccessToken);

        var userB = await fixture.CreateUserAsync();
        var sessionB = await fixture.LoginAsync(userB.Email, userB.Password);
        var workspaceB = await fixture.CreateWorkspaceAsync(sessionB.AccessToken);

        // Refresh tokens are bearer credentials, so presenting B's token succeeds — but it must mint
        // a token for B. Whose workspaces come back is the black-box proof of whose token it is.
        var response = await fixture.RefreshAsync(sessionB.RefreshToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotated = await ServicesFixture.ReadSessionAsync(response);
        var visible = await fixture.GetWorkspaceRefsAsync(rotated.AccessToken);

        visible.Should().Contain(workspaceB);
        visible.Should().NotContain(workspaceA, "rotation must never widen who the token speaks for");
    }

    // ── Interaction with logout and deactivation ──────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_RT_11_Refresh_AfterLogout_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var logout = await fixture.LogoutAsync(session.RefreshToken);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await fixture.RefreshAsync(session.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_TOKEN_INACTIVE");
    }

    [SkippableFact]
    public async Task AUTH_RT_18_RefreshAfterLogout_AlsoRevokesSiblingSessions()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var deviceOne = await fixture.LoginAsync(user.Email, user.Password);
        var deviceTwo = await fixture.LoginAsync(user.Email, user.Password);

        await fixture.LogoutAsync(deviceOne.RefreshToken);

        // Logout marks the token revoked, and the rotation path cannot tell a deliberate logout from
        // a stolen-token replay — both are "a retired token came back". So one device retrying its
        // refresh after signing out takes every other device down with it, and raises a
        // RefreshTokenReplay security alert while doing it.
        await fixture.RefreshAsync(deviceOne.RefreshToken);

        var sibling = await fixture.RefreshAsync(deviceTwo.RefreshToken);

        sibling.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "documents current behaviour: a benign post-logout retry is indistinguishable from theft");
    }

    [SkippableFact]
    public async Task AUTH_RT_12_Refresh_AfterLogoutAll_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var deviceOne = await fixture.LoginAsync(user.Email, user.Password);
        var deviceTwo = await fixture.LoginAsync(user.Email, user.Password);

        var logoutAll = await fixture.LogoutAllAsync(deviceOne.AccessToken);
        logoutAll.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.RefreshAsync(deviceOne.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        (await fixture.RefreshAsync(deviceTwo.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "logout-all means every device, not just this one");
    }

    [SkippableFact]
    public async Task AUTH_RT_13_Refresh_AfterDeactivation_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);

        // Invited into the admin's tenant: registration alone would put them in a tenant of their
        // own, where the admin holds nothing and cannot deactivate them.
        var user = await fixture.CreateUserInTenantAsync(adminToken, tenantRef);
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var userRef = await fixture.FindTenantMemberRefAsync(adminToken, tenantRef, user.Email);
        userRef.Should().NotBeNull();

        var deactivate = await fixture.SetUserActiveAsync(adminToken, tenantRef, userRef!.Value, isActive: false);
        deactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await fixture.RefreshAsync(session.RefreshToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // AUTH_TOKEN_INACTIVE, not AUTH_USER_INACTIVE: deactivation revokes every refresh token
        // first, so the rotation path trips on the revoked token and never reaches the IsActive
        // check. The user-inactive branch is effectively unreachable on this path — a token issued
        // after deactivation cannot exist, because login is already refused.
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_TOKEN_INACTIVE");
    }

    [SkippableFact]
    public async Task AUTH_RT_14_Refresh_ExpiredToken_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // Refresh tokens live 7 days and nothing in the API can age one. Reaching this needs either
        // an injectable clock in the host or a seeded row with a past Expires; until one exists the
        // expiry branch of RefreshTokenAsync is untested.
        Skip.If(true, "needs clock control or a seeded expired token — see §5 AUTH_RT_14.");
    }
}
