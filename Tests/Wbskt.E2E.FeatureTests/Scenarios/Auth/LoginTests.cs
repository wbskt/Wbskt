using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 4 of Docs/Auth.Host.E2E.Scenarios.md — <c>POST /api/auth/login</c>.
///
/// The governing property is that every failure looks the same. Login verifies a credential rather
/// than accepting one, so anything that distinguishes *why* it failed — wrong password, no such
/// account, malformed identifier — hands an anonymous caller a way to test which addresses are
/// registered. That is why <c>LoginRequest</c> deliberately carries no <c>[EmailAddress]</c>:
/// rejecting a malformed identifier with a 400 would say "that was the wrong shape" where every
/// other failure says 401, and it would break the moment sign-in accepts a username too.
///
/// AUTH_LOG_06 is the one place that property does not hold, and it is asserted as-is.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class LoginTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task AUTH_LOG_01_CorrectCredentials_ReturnsBothTokens()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var session = await fixture.LoginAsync(user.Email, user.Password);

        session.AccessToken.Should().NotBeNullOrWhiteSpace();
        session.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task AUTH_LOG_02_AccessTokenAuthenticatesAProtectedCall()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl("/api/workspaces"), session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_LOG_03_TwoLoginsIssueIndependentSessions()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var first = await fixture.LoginAsync(user.Email, user.Password);
        var second = await fixture.LoginAsync(user.Email, user.Password);

        second.RefreshToken.Should().NotBe(first.RefreshToken);

        // Multi-device: signing in on a second device must not invalidate the first.
        (await fixture.RefreshAsync(first.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Every failure looks the same ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_LOG_04_WrongPassword_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.LoginRawAsync(user.Email, $"wrong-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [SkippableFact]
    public async Task AUTH_LOG_05_UnknownAddressAndWrongPassword_AreIndistinguishable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var wrongPassword = await fixture.LoginRawAsync(user.Email, $"wrong-{Guid.NewGuid():N}");
        var unknownAccount = await fixture.LoginRawAsync($"nobody-{Guid.NewGuid():N}@test.local", "anything-at-all");

        // The assertion that matters is the comparison, not the constant: if these ever diverge,
        // the endpoint becomes a way to test whether an address has an account.
        unknownAccount.StatusCode.Should().Be(wrongPassword.StatusCode);
        (await ServicesFixture.ReadErrorCodeAsync(unknownAccount))
            .Should().Be(await ServicesFixture.ReadErrorCodeAsync(wrongPassword));
    }

    [SkippableFact]
    public async Task AUTH_LOG_07_PasswordIsCaseSensitive()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.LoginRawAsync(user.Email, user.Password.ToUpperInvariant());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_LOG_10_MalformedIdentifier_Returns401NotValidationError()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var malformed = await fixture.LoginRawAsync("not-an-email-at-all", "some-password");
        var wrongPassword = await fixture.LoginRawAsync(user.Email, $"wrong-{Guid.NewGuid():N}");

        // LoginRequest carries no [EmailAddress] by design. A 400 here would tell a caller the value
        // was the wrong *shape* rather than simply wrong, and it would have to be undone the moment
        // sign-in accepts a username as well as an address.
        malformed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        malformed.StatusCode.Should().Be(wrongPassword.StatusCode);
    }

    [SkippableFact]
    public async Task AUTH_LOG_12_InjectionStringAsIdentifier_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.LoginRawAsync("' OR 1=1 --", "anything");

        // Never 200, and never a 500 that would prove the value reached the query unparameterised.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_LOG_06_InactiveAccount_IsDistinguishable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);

        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);
        var memberRef = await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, member.Email);
        memberRef.Should().NotBeNull();

        await fixture.SetUserActiveAsync(admin.Token, tenantRef, memberRef!.Value, isActive: false);

        var response = await fixture.LoginRawAsync(member.Email, member.Password);

        // The one place the uniform-failure property does not hold: AUTH_USER_INACTIVE tells a
        // caller the address exists and is merely disabled, where every other failure says only
        // "invalid credentials". Asserted as-is so the leak is a recorded decision rather than an
        // accident; closing it means collapsing this into AUTH_INVALID_CREDENTIALS.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_USER_INACTIVE");
    }

    // ── Input bounds ──────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_LOG_09_MissingPassword_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/auth/login"),
            body: new { Email = "someone@test.local" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_LOG_11_EmptyBody_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(
            HttpMethod.Post, ServicesFixture.AuthUrl("/api/auth/login"), body: new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_LOG_11b_OverlongIdentifier_IsBoundedBeforeTheHasher()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // Length and presence are still enforced even though format is not — they cost nothing and
        // keep junk out of the password hasher and the lookup.
        var response = await fixture.LoginRawAsync(new string('e', 101), "some-password");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_LOG_13_LoginIsAnonymous()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.LoginRawAsync(user.Email, user.Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_LOG_14_FailedLoginDoesNotDisturbAnExistingSession()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        await fixture.LoginRawAsync(user.Email, $"wrong-{Guid.NewGuid():N}");

        // A wrong guess must not be a denial-of-service against the person's live sessions.
        (await fixture.RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
