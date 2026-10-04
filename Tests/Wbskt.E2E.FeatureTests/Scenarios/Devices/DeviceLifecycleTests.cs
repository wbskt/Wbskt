using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// Section 12 of Docs/Auth.Host.E2E.Scenarios.md — deleting a device, rotating its secret or its
/// policy's PIN, and approving or revoking many pending devices at once.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DeviceLifecycleTests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SecretDto(Guid ClientRefId, string Secret);

    private sealed record PolicyDto(Guid RefId, string Pin);

    private sealed record FailureDto(Guid ClientRefId, string Code, string Message);

    private sealed record BulkDto(List<Guid> Updated, List<FailureDto> Failed);

    // ── Delete ────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_DEL_01_ADeletedDevice_CanNoLongerSignIn_OrBeFound()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, "to-delete");

        (await Send(HttpMethod.Delete, workspace, $"clients/{clientRef}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ClientLoginAsync(clientRef, secret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, workspace, $"clients/{clientRef}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task DEV_DEL_02_AnotherWorkspacesDevice_IsForbidden_AndSurvives()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (ownerToken, ownerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(ownerToken, ownerWorkspace);
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, "not-yours");
        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();

        var response = await Send(HttpMethod.Delete, strangerWorkspace, $"clients/{clientRef}", strangerToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("CLIENT_UNAUTHORIZED");
        (await ClientLoginAsync(clientRef, secret)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Timestamps ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_TIME_01_TimesFromTheDatabase_AreWrittenAsUtc()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "timestamps");

        var response = await Send(HttpMethod.Get, workspace, $"clients/{clientRef}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Read back from SQL with no kind; without the Z a browser shows it in its own time zone.
        var createdAt = body.RootElement.GetProperty("createdAt").GetString();
        createdAt.Should().EndWith("Z");
        DateTimeOffset.Parse(createdAt!).Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
    }

    // ── Rotate secret ─────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_SEC_01_RotatingTheSecret_RetiresTheOldOne()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, oldSecret) = await fixture.RegisterClientAsync(pin, "rotating");

        var response = await Send(HttpMethod.Post, workspace, $"clients/{clientRef}/rotate-secret", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await response.Content.ReadFromJsonAsync<SecretDto>(JsonOptions);

        rotated!.ClientRefId.Should().Be(clientRef);
        rotated.Secret.Should().NotBe(oldSecret);
        (await ClientLoginAsync(clientRef, oldSecret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ClientLoginAsync(clientRef, rotated.Secret)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Rotate PIN ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_PIN_01_RotatingThePin_StopsTheOldOne_AndKeepsDevices()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (policyRef, oldPin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, secret) = await fixture.RegisterClientAsync(oldPin, "before-rotation");

        var response = await Send(HttpMethod.Post, workspace, $"registration-policies/{policyRef}/rotate-pin", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var policy = await response.Content.ReadFromJsonAsync<PolicyDto>(JsonOptions);

        policy!.Pin.Should().NotBe(oldPin);
        (await RegisterRawAsync(oldPin)).IsSuccessStatusCode.Should().BeFalse();
        (await RegisterRawAsync(policy.Pin)).IsSuccessStatusCode.Should().BeTrue();
        (await ClientLoginAsync(clientRef, secret)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Bulk status ───────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_BULK_01_ApprovingABatch_ReportsEachDevice()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: false);
        var (first, firstSecret) = await fixture.RegisterClientAsync(pin, "pending-1");
        var (second, _) = await fixture.RegisterClientAsync(pin, "pending-2");
        var unknown = Guid.NewGuid();

        var response = await Send(HttpMethod.Patch, workspace, "clients/status", token,
            new { ClientRefIds = new[] { first, second, unknown }, Status = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkDto>(JsonOptions);
        result!.Updated.Should().BeEquivalentTo([first, second]);
        result.Failed.Should().ContainSingle().Which.Should().Match<FailureDto>(f => f.ClientRefId == unknown && f.Code == "CLIENT_UNAUTHORIZED");
        (await ClientLoginAsync(first, firstSecret)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task DEV_BULK_02_ABatchStopsAtThePolicyLimit()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var created = await Send(HttpMethod.Post, workspace, "registration-policies", token,
            new { Name = $"e2e-limit-{Guid.NewGuid():N}", MaxClients = 1, AutoApproval = false });
        created.EnsureSuccessStatusCode();
        var policy = await created.Content.ReadFromJsonAsync<PolicyDto>(JsonOptions);
        var (first, _) = await fixture.RegisterClientAsync(policy!.Pin, "pending-1");
        var (second, _) = await fixture.RegisterClientAsync(policy.Pin, "pending-2");

        var response = await Send(HttpMethod.Patch, workspace, "clients/status", token,
            new { ClientRefIds = new[] { first, second }, Status = 1 });

        var result = await response.Content.ReadFromJsonAsync<BulkDto>(JsonOptions);
        result!.Updated.Should().Equal(first);
        result.Failed.Should().ContainSingle().Which.Code.Should().Be("POLICY_LIMIT_REACHED");
    }

    [SkippableFact]
    public async Task DEV_BULK_03_AnEmptyOrOversizedBatch_Is400()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();

        (await Send(HttpMethod.Patch, workspace, "clients/status", token, new { ClientRefIds = Array.Empty<Guid>(), Status = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Patch, workspace, "clients/status", token,
                new { ClientRefIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray(), Status = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> Send(HttpMethod method, Guid workspace, string path, string token, object? body = null) =>
        fixture.SendAsync(method, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}"), token, body);

    private Task<HttpResponseMessage> ClientLoginAsync(Guid clientRef, string secret) =>
        fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl("/api/client-auth/login"), null, new { ClientRefId = clientRef, Secret = secret });

    private Task<HttpResponseMessage> RegisterRawAsync(string pin) =>
        fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl("/api/client-registrations/initiate"), null, new { Pin = pin, Name = "after-rotation" });
}
