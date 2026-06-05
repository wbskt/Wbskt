using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests;

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

        // ── 1. Register & login a unique user ────────────────────────────────
        var (token, workspaceRef) = await fixture.RegisterAndLoginUserAsync();

        token.Should().NotBeNullOrWhiteSpace("login must return a non-empty access token");
        workspaceRef.Should().NotBeEmpty("a workspace must be available after registration");

        // ── 2. Create an AutoApproval registration policy ────────────────────
        var (policyRef, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        policyRef.Should().NotBeEmpty("policy creation must return a valid RefId");
        pin.Should().NotBeNullOrWhiteSpace("policy must come with a non-empty PIN");

        // ── 3. Initiate device registration via PIN ───────────────────────────
        var deviceName = $"e2e-device-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        clientRefId.Should().NotBeEmpty("client registration must return a valid ClientRefId");
        secret.Should().NotBeNullOrWhiteSpace("client registration must return a non-empty secret");

        // ── 4. Client auth login ──────────────────────────────────────────────
        var clientToken = await fixture.LoginClientAsync(clientRefId, secret);

        clientToken.Should().NotBeNullOrWhiteSpace("client login must return a non-empty access token");
    }
}
