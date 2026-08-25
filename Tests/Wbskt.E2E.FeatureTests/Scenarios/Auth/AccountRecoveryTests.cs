using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// <c>POST /api/auth/forgot-password</c>, <c>reset-password</c>, <c>verify-email</c> and
/// <c>resend-verification</c>.
///
/// What is asserted here is the non-disclosure property, because that is what these endpoints are
/// mostly for: all four are anonymous, and three of them answer identically whether or not the
/// address they were given exists. An endpoint that answered differently would let anyone enumerate
/// the platform's users at the rate limiter's pace.
///
/// <para>
/// <b>The round trip is not covered here.</b> Following a link end to end needs the mail that carries
/// it, and there is no inbox in this environment to read one out of. Standing a mail catcher
/// (MailHog exposes an HTTP API for exactly this) next to the hosts would let this file assert
/// register → verify → sign in → forget → reset → old sessions refused, which is the assertion that
/// actually matters. Until then the unit suite covers the decisions and this file covers the edges.
/// </para>
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class AccountRecoveryTests(ServicesFixture fixture)
{
    private static string ForgotUrl => ServicesFixture.AuthUrl("/api/auth/forgot-password");
    private static string ResetUrl => ServicesFixture.AuthUrl("/api/auth/reset-password");
    private static string VerifyUrl => ServicesFixture.AuthUrl("/api/auth/verify-email");
    private static string ResendUrl => ServicesFixture.AuthUrl("/api/auth/resend-verification");

    // ── Non-disclosure ────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_RECOVER_01_ForgotPassword_ForAnAddressWithAnAccount_Returns204()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (username, email, password) = ServicesFixture.NewCredentials();
        await fixture.RegisterAsync(username, email, password);

        var response = await fixture.SendAsync(HttpMethod.Post, ForgotUrl, body: new { Email = email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task AUTH_RECOVER_02_ForgotPassword_ForAnUnknownAddress_AnswersTheSameWay()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (_, email, _) = ServicesFixture.NewCredentials();

        var response = await fixture.SendAsync(HttpMethod.Post, ForgotUrl, body: new { Email = email });

        // Byte for byte what a registered address gets. This assertion is the whole point of the
        // endpoint's design.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task AUTH_RECOVER_03_ResendVerification_ForAnUnknownAddress_AnswersTheSameWay()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (_, email, _) = ServicesFixture.NewCredentials();

        var response = await fixture.SendAsync(HttpMethod.Post, ResendUrl, body: new { Email = email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Deliberately no <c>[EmailAddress]</c> on the request model: a 400 for a malformed address
    /// would be the one answer that differs, and shape is something an attacker controls.
    /// </summary>
    [SkippableFact]
    public async Task AUTH_RECOVER_04_ForgotPassword_ForAMalformedAddress_StillAnswersTheSameWay()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(HttpMethod.Post, ForgotUrl, body: new { Email = "not-an-address" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Token redemption ──────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_RECOVER_05_ResetPassword_WithAnUnknownToken_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(
            HttpMethod.Post, ResetUrl,
            body: new { Token = "not-a-real-token", NewPassword = "a perfectly long password" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("RESET_TOKEN_INVALID");
    }

    [SkippableFact]
    public async Task AUTH_RECOVER_06_VerifyEmail_WithAnUnknownToken_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(HttpMethod.Post, VerifyUrl, body: new { Token = "not-a-real-token" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("VERIFICATION_TOKEN_INVALID");
    }

    /// <summary>
    /// A reset must not be a way around the password rules that registration enforces. Both sides
    /// carry the same <c>StringLength(128, MinimumLength = 12)</c>.
    /// </summary>
    [SkippableFact]
    public async Task AUTH_RECOVER_07_ResetPassword_WithATooShortPassword_IsRejectedBeforeTheToken()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(
            HttpMethod.Post, ResetUrl,
            body: new { Token = "not-a-real-token", NewPassword = new string('a', 11) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Model validation, not token validation — so no error code from the service layer.
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().NotBe("RESET_TOKEN_INVALID");
    }

    // ── Posture ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// All four are anonymous. resend-verification in particular cannot be authenticated: sign-in
    /// requires a confirmed address, so an account that needs this endpoint is by definition one
    /// that cannot obtain a token with which to call it.
    /// </summary>
    [SkippableTheory]
    [InlineData("/api/auth/forgot-password")]
    [InlineData("/api/auth/resend-verification")]
    public async Task AUTH_RECOVER_08_TheRecoveryEndpointsAreReachableWithoutAToken(string path)
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (_, email, _) = ServicesFixture.NewCredentials();

        var response = await fixture.SendAsync(HttpMethod.Post, ServicesFixture.AuthUrl(path), body: new { Email = email });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
