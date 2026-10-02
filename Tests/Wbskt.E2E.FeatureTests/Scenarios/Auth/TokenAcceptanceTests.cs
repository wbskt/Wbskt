using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 8 of Docs/Auth.Host.E2E.Scenarios.md — what the auth host will and will not accept as
/// proof of identity.
///
/// The scenarios that matter most here are AUTH_TK_06 and AUTH_TK_07. A *client* token is minted by
/// the management host with its own key, as issuer wbskt-management for audience wbskt-socket; this
/// host accepts only wbskt-auth tokens for wbskt-api, so the token fails on issuer, audience and
/// signature alike. Before the keys were split every host shared one secret and checked neither
/// claim, and the refusal rested on a client's subject being a Guid where the identity middleware
/// needs an int. These tests pin the refusal whichever layer provides it.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class TokenAcceptanceTests(ServicesFixture fixture)
{
    /// <summary>Any authenticated endpoint will do; this one needs only a valid identity.</summary>
    private static string ProtectedEndpoint => ServicesFixture.AuthUrl("/api/workspaces");

    [SkippableFact]
    public async Task AUTH_TK_01_NoAuthorizationHeader_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(HttpMethod.Get, ProtectedEndpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_TK_02_GarbageBearerToken_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(HttpMethod.Get, ProtectedEndpoint, "not-a-jwt");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_TK_03_TokenSignedWithWrongKey_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // Structurally perfect and unexpired — only the signature is wrong.
        var forged = ServicesFixture.MintJwt(
            signingKey: $"an-attacker-controlled-key-{Guid.NewGuid():N}",
            subject: "1",
            lifetime: TimeSpan.FromMinutes(30));

        var response = await fixture.SendAsync(HttpMethod.Get, ProtectedEndpoint, forged);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the signing key is the only thing standing between a caller and any identity they like");
    }

    [SkippableFact]
    public async Task AUTH_TK_05_UnsignedToken_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // The classic alg:none downgrade — a well-formed token with the signature omitted entirely.
        var unsigned = ServicesFixture.MintJwt(signingKey: null, subject: "1", lifetime: TimeSpan.FromMinutes(30));

        var response = await fixture.SendAsync(HttpMethod.Get, ProtectedEndpoint, unsigned);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_TK_08_AuthorizationHeaderWithoutBearerPrefix_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        // A real, valid token — presented under a scheme the host does not implement.
        var response = await fixture.SendWithRawAuthorizationAsync(HttpMethod.Get, ProtectedEndpoint, user.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_TK_06_ClientToken_IsRefusedByTheAuthHost()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var clientToken = await MintClientTokenAsync();

        var response = await fixture.SendAsync(HttpMethod.Get, ProtectedEndpoint, clientToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a device credential must never be accepted as a user identity — it is signed by another "
            + "issuer, for another audience");
    }

    [SkippableFact]
    public async Task AUTH_TK_07_ClientToken_IsRefusedByTenantAdministration()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var clientToken = await MintClientTokenAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl("/api/tenants"), clientToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "tenant administration is the most privileged surface here and must be no more reachable "
            + "with a client token than anything else");
    }

    [SkippableFact]
    public async Task AUTH_TK_10_AccessTokenOfDeactivatedUser_RemainsValidUntilExpiry()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);

        // Invited into the admin's tenant: registration alone would put them in a tenant of their
        // own, where the admin holds nothing and cannot deactivate them.
        var user = await fixture.CreateUserInTenantAsync(adminToken, tenantRef);
        var userRef = await fixture.FindTenantMemberRefAsync(adminToken, tenantRef, user.Email);
        userRef.Should().NotBeNull();

        await fixture.SetUserActiveAsync(adminToken, tenantRef, userRef!.Value, isActive: false);

        var response = await fixture.SendAsync(HttpMethod.Get, ProtectedEndpoint, user.Token);

        // Deactivation revokes refresh tokens; an access token is self-contained and nothing
        // consults the database on the authentication path. The account is therefore reachable for
        // up to the token's remaining hour. Documented deliberately — this is the window an
        // operator disabling a compromised account is actually working with.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "no revocation check exists on the access-token path");
    }

    /// <summary>
    /// Drives the real client-onboarding flow on the management host to obtain a genuine client
    /// token: seeded admin → auto-approval policy → PIN registration → client login.
    /// </summary>
    private async Task<string> MintClientTokenAsync()
    {
        var (adminToken, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(adminToken, workspaceRef, autoApproval: true);

        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, $"e2e-token-probe-{Guid.NewGuid():N}");

        return await fixture.LoginClientAsync(clientRefId, secret);
    }
}
