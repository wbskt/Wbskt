using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 3 of Docs/Auth.Host.E2E.Scenarios.md — <c>POST /api/auth/register</c>.
///
/// Registration is anonymous and provisions a whole tenant, so its input validation is the outermost
/// boundary in the system: everything here is rejected before any account exists. The invitation
/// token is optional — an account gets its own tenant either way — and every way it can be invalid
/// answers identically, since an anonymous endpoint that distinguished them would be an oracle for
/// probing outstanding invitations.
///
/// What registration *produces* (tenant, roles, default workspace) is covered by
/// TenantLifecycleTests; this file is about what it accepts and refuses.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class RegistrationTests(ServicesFixture fixture)
{
    private static string RegisterUrl => ServicesFixture.AuthUrl("/api/auth/register");

    [SkippableFact]
    public async Task AUTH_REG_01_ValidRegistration_Returns204()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();

        var response = await fixture.RegisterAsync(username, email, password);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task AUTH_REG_02_RegisteredUser_CanLogInImmediately()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();
        await fixture.RegisterAsync(username, email, password);

        var session = await fixture.LoginAsync(email, password);

        session.AccessToken.Should().NotBeNullOrWhiteSpace();
        session.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task AUTH_REG_04_DuplicateEmail_Returns409()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();
        await fixture.RegisterAsync(username, email, password);

        var (otherUsername, _, _) = ServicesFixture.NewCredentials();
        var response = await fixture.RegisterAsync(otherUsername, email, password);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_USER_CONFLICT");
    }

    [SkippableFact]
    public async Task AUTH_REG_05_DuplicateUsername_Returns409()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();
        await fixture.RegisterAsync(username, email, password);

        var (_, otherEmail, _) = ServicesFixture.NewCredentials();
        var response = await fixture.RegisterAsync(username, otherEmail, password);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Deliberately the same code as a duplicate email: the endpoint is anonymous, and telling a
        // caller *which* field collided would let them enumerate registered usernames.
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_USER_CONFLICT");
    }

    // ── Password bounds ───────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_REG_06_PasswordOfElevenCharacters_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, _) = ServicesFixture.NewCredentials();

        var response = await fixture.RegisterAsync(username, email, new string('a', 11));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_REG_07_PasswordOfExactlyTwelveCharacters_IsAccepted()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, _) = ServicesFixture.NewCredentials();

        // The boundary itself — off-by-one here would silently weaken the minimum.
        var response = await fixture.RegisterAsync(username, email, new string('a', 12));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task AUTH_REG_08_PasswordOverOneTwentyEight_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, _) = ServicesFixture.NewCredentials();

        var response = await fixture.RegisterAsync(username, email, new string('a', 129));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Username bounds ───────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_REG_09_UsernameOfTwoCharacters_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (_, email, password) = ServicesFixture.NewCredentials();

        var response = await fixture.RegisterAsync("ab", email, password);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_REG_10_UsernameOfExactlyThreeCharacters_IsAccepted()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (_, email, password) = ServicesFixture.NewCredentials();

        // Three random characters collide often enough to matter across repeated runs, so a conflict
        // here still proves the length was accepted.
        var response = await fixture.RegisterAsync(Guid.NewGuid().ToString("N")[..3], email, password);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task AUTH_REG_11_UsernameOverFifty_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (_, email, password) = ServicesFixture.NewCredentials();

        var response = await fixture.RegisterAsync(new string('u', 51), email, password);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Email ─────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_REG_12_MalformedEmail_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, _, password) = ServicesFixture.NewCredentials();

        // Unlike login, registration *does* validate the format — it is accepting a new identifier
        // rather than checking an existing one, so a malformed address is a caller mistake worth
        // naming rather than a credential that simply does not match.
        var response = await fixture.RegisterAsync(username, "not-an-email", password);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_REG_13_EmailOverOneHundred_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, _, password) = ServicesFixture.NewCredentials();
        var overlong = $"{new string('e', 95)}@test.local";

        var response = await fixture.RegisterAsync(username, overlong, password);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_REG_18_EmailDifferingOnlyByCase_DocumentsItsBehaviour()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();
        await fixture.RegisterAsync(username, email, password);

        var (otherUsername, _, _) = ServicesFixture.NewCredentials();
        var response = await fixture.RegisterAsync(otherUsername, email.ToUpperInvariant(), password);

        // Which way this goes depends on the column's collation, and the two outcomes are very
        // different products: a conflict means addresses are one identity, success means two
        // accounts can share an inbox. Asserted loosely so the answer is recorded either way; if it
        // is 409 the collation is case-insensitive, and that is the behaviour to keep.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.NoContent);
    }

    // ── Malformed requests ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_REG_14_MissingPassword_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, _) = ServicesFixture.NewCredentials();

        var response = await fixture.SendAsync(
            HttpMethod.Post, RegisterUrl, body: new { Username = username, Email = email });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_REG_15_EmptyBody_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(HttpMethod.Post, RegisterUrl, body: new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_REG_16_MalformedJson_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendRawBodyAsync(
            HttpMethod.Post, RegisterUrl, "{\"username\": ", "application/json");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_REG_17_UnsupportedContentType_Returns415()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendRawBodyAsync(
            HttpMethod.Post, RegisterUrl, "username=x&email=y", "text/plain");

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    // ── Concurrency and exposure ──────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_REG_19_ConcurrentRegistrationsOfOneEmail_YieldExactlyOneAccount()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();
        var (otherUsername, _, _) = ServicesFixture.NewCredentials();

        var responses = await Task.WhenAll(
            fixture.RegisterAsync(username, email, password),
            fixture.RegisterAsync(otherUsername, email, password));

        // The unique constraint is the arbiter — two accounts sharing an address would give two
        // tenants reachable by one identity.
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }

    [SkippableFact]
    public async Task AUTH_REG_20_RegistrationIsAnonymous()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();

        // No Authorization header is sent anywhere in this file; asserting it explicitly so that
        // adding [Authorize] to the controller would fail here rather than in a console.
        var response = await fixture.RegisterAsync(username, email, password);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task AUTH_REG_21_PasswordIsNeverEchoedBack()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();

        var response = await fixture.RegisterAsync(username, email, password);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(password);
    }

    // ── Invitation token ──────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_REG_22_UnknownInvitationToken_IsRejectedUniformly()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();

        var response = await fixture.RegisterAsync(
            username, email, password, invitationToken: Guid.NewGuid().ToString("N"));

        // One answer for every way an invitation can fail — unknown, expired, revoked, spent, or
        // addressed to someone else. Registration is anonymous, so distinguishing them would turn
        // it into an oracle for probing which invitations exist.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("INVITATION_INVALID");
    }

    [SkippableFact]
    public async Task AUTH_REG_23_InvitationForADifferentAddress_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);

        var (invitedUsername, invitedEmail, invitedPassword) = ServicesFixture.NewCredentials();
        var token = await fixture.InviteAsync(admin.Token, tenantRef, invitedEmail);

        // A leaked link is not by itself enough to join — the account's address must match.
        var (otherUsername, otherEmail, _) = ServicesFixture.NewCredentials();
        var response = await fixture.RegisterAsync(otherUsername, otherEmail, invitedPassword, token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("INVITATION_INVALID");

        // The invitation must survive the failed attempt, or a wrong guess would burn it.
        var accepted = await fixture.RegisterAsync(invitedUsername, invitedEmail, invitedPassword, token);
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task AUTH_REG_24_OverlongInvitationToken_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();

        var response = await fixture.RegisterAsync(username, email, password, new string('t', 256));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
