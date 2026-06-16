using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Task 2 — Auth, registration-policy, and client-registration happy path.
///
/// Requires all four dev hosts running. Skips gracefully when hosts are down.
/// Each run uses unique credentials so tests are fully isolated and repeatable.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class AuthAndRegistrationTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task RegisterLoginCreatePolicyRegisterClient_AllSucceed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // ── 1. A brand-new user can register & login ─────────────────────────
        var newUserToken = await fixture.RegisterAndLoginNewUserAsync();
        newUserToken.Should().NotBeNullOrWhiteSpace("registration + login must return a non-empty access token");

        // ── 2. Use the seeded admin (all permissions, Default Workspace) for
        //       the permission-gated policy + client-registration flow ────────
        var (adminToken, workspaceRef) = await fixture.LoginAsAdminAsync();

        adminToken.Should().NotBeNullOrWhiteSpace("admin login must return a non-empty access token");
        workspaceRef.Should().NotBeEmpty("the admin must have a default workspace");

        // ── 3. Create an AutoApproval registration policy ────────────────────
        var (policyRef, pin) = await fixture.CreatePolicyAsync(adminToken, workspaceRef, autoApproval: true);

        policyRef.Should().NotBeEmpty("policy creation must return a valid RefId");
        pin.Should().NotBeNullOrWhiteSpace("policy must come with a non-empty PIN");

        // ── 4. Initiate device registration via PIN ───────────────────────────
        var deviceName = $"e2e-device-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        clientRefId.Should().NotBeEmpty("client registration must return a valid ClientRefId");
        secret.Should().NotBeNullOrWhiteSpace("client registration must return a non-empty secret");

        // ── 5. Client auth login ──────────────────────────────────────────────
        var clientToken = await fixture.LoginClientAsync(clientRefId, secret);

        clientToken.Should().NotBeNullOrWhiteSpace("client login must return a non-empty access token");
    }
}
