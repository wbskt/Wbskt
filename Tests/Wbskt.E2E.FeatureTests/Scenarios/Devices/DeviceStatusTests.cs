using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// Changing one device's status, renaming it and reading its state, plus the bulk status cases
/// DeviceLifecycleTests leaves out: which devices get a policy's last places, and how a batch
/// reports repeated, unchanged, missing and foreign devices. All of these go through one database
/// call (dbo.Client_UpdateStatuses or Client_GetDetailBy_RefId); these pin what the API answers.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DeviceStatusTests(ServicesFixture fixture)
{
    private const int Pending = 0;
    private const int Registered = 1;
    private const int Revoked = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record PolicyDto(Guid RefId, string Pin);

    private sealed record FailureDto(Guid ClientRefId, string Code, string Message);

    private sealed record BulkDto(List<Guid> Updated, List<FailureDto> Failed);

    // ── One device's status ───────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_STATUS_01_ApprovingAPendingDevice_LetsItSignIn()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: false);
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, "pending");
        (await ClientLoginAsync(clientRef, secret)).IsSuccessStatusCode.Should().BeFalse();

        var response = await SetStatusAsync(workspace, clientRef, token, Registered);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StatusOfAsync(workspace, clientRef, token)).Should().Be(Registered);
        (await ClientLoginAsync(clientRef, secret)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task DEV_STATUS_02_RevokingADevice_StopsItSigningIn()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, "to-revoke");

        (await SetStatusAsync(workspace, clientRef, token, Revoked)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StatusOfAsync(workspace, clientRef, token)).Should().Be(Revoked);
        (await ClientLoginAsync(clientRef, secret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task DEV_STATUS_03_SettingTheStatusADeviceAlreadyHas_Succeeds_AndChangesNothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, "already-registered");

        (await SetStatusAsync(workspace, clientRef, token, Registered)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StatusOfAsync(workspace, clientRef, token)).Should().Be(Registered);
        (await ClientLoginAsync(clientRef, secret)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task DEV_STATUS_04_AnUnknownDevice_Is404()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();

        var response = await SetStatusAsync(workspace, Guid.NewGuid(), token, Registered);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("CLIENT_NOT_FOUND");
    }

    [SkippableFact]
    public async Task DEV_STATUS_05_AnotherWorkspacesDevice_Is404_AndKeepsItsStatus()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (ownerToken, ownerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(ownerToken, ownerWorkspace);
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, "not-yours");
        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();

        var response = await SetStatusAsync(strangerWorkspace, clientRef, strangerToken, Revoked);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("CLIENT_NOT_FOUND");
        (await StatusOfAsync(ownerWorkspace, clientRef, ownerToken)).Should().Be(Registered);
        (await ClientLoginAsync(clientRef, secret)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task DEV_STATUS_06_ApprovingIntoAFullPolicy_IsRefused_AndTheDeviceStaysPending()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var pin = await CreateLimitedPolicyAsync(token, workspace, maxClients: 1);
        var (first, _) = await fixture.RegisterClientAsync(pin, "pending-1");
        var (second, _) = await fixture.RegisterClientAsync(pin, "pending-2");
        (await SetStatusAsync(workspace, first, token, Registered)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await SetStatusAsync(workspace, second, token, Registered);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("POLICY_LIMIT_REACHED");
        (await StatusOfAsync(workspace, second, token)).Should().Be(Pending);
    }

    // ── Rename ────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_NAME_01_RenamingADevice_ChangesItsName()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "old-name");

        (await Send(HttpMethod.Patch, workspace, $"clients/{clientRef}/name", token, new { Name = "  greenhouse  " }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await DetailAsync(workspace, clientRef, token)).GetProperty("name").GetString().Should().Be("greenhouse");
    }

    [SkippableFact]
    public async Task DEV_NAME_02_RenamingAnUnknownDevice_OrAnotherWorkspaces_Is404()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (ownerToken, ownerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(ownerToken, ownerWorkspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "keeps-its-name");
        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();

        var missing = await Send(HttpMethod.Patch, strangerWorkspace, $"clients/{Guid.NewGuid()}/name", strangerToken, new { Name = "x" });
        var foreign = await Send(HttpMethod.Patch, strangerWorkspace, $"clients/{clientRef}/name", strangerToken, new { Name = "x" });

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(missing)).Should().Be("CLIENT_NOT_FOUND");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(foreign)).Should().Be("CLIENT_NOT_FOUND");
        (await DetailAsync(ownerWorkspace, clientRef, ownerToken)).GetProperty("name").GetString().Should().Be("keeps-its-name");
    }

    // ── State ─────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_STATE_01_ANewDevicesState_IsEmpty_AndOnlyItsWorkspaceCanReadIt()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (ownerToken, ownerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(ownerToken, ownerWorkspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "no-state-yet");
        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();

        var own = await Send(HttpMethod.Get, ownerWorkspace, $"clients/{clientRef}/state", ownerToken);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = JsonDocument.Parse(await own.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        }

        var missing = await Send(HttpMethod.Get, ownerWorkspace, $"clients/{Guid.NewGuid()}/state", ownerToken);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(missing)).Should().Be("CLIENT_NOT_FOUND");

        var foreign = await Send(HttpMethod.Get, strangerWorkspace, $"clients/{clientRef}/state", strangerToken);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(foreign)).Should().Be("CLIENT_NOT_FOUND");
    }

    // ── Bulk status ───────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_BULK_04_APolicysLastPlaces_GoToTheEarliestDevicesInTheBatch()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var pin = await CreateLimitedPolicyAsync(token, workspace, maxClients: 2);
        var (a, _) = await fixture.RegisterClientAsync(pin, "pending-a");
        var (b, _) = await fixture.RegisterClientAsync(pin, "pending-b");
        var (c, _) = await fixture.RegisterClientAsync(pin, "pending-c");

        var result = await BulkAsync(workspace, token, [c, a, b], Registered);

        result.Updated.Should().Equal(c, a);
        result.Failed.Should().ContainSingle().Which.Should().Match<FailureDto>(f => f.ClientRefId == b && f.Code == "POLICY_LIMIT_REACHED");
        (await StatusOfAsync(workspace, b, token)).Should().Be(Pending);
    }

    [SkippableFact]
    public async Task DEV_BULK_05_ABatch_ReportsRepeatedUnchangedMissingAndForeignDevices()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: false);
        var (pending, _) = await fixture.RegisterClientAsync(pin, "pending");
        var (_, autoPin) = await fixture.CreatePolicyAsync(token, workspace);
        var (registered, _) = await fixture.RegisterClientAsync(autoPin, "already-registered");
        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, strangerPin) = await fixture.CreatePolicyAsync(strangerToken, strangerWorkspace, autoApproval: false);
        var (foreign, _) = await fixture.RegisterClientAsync(strangerPin, "not-yours");
        var missing = Guid.NewGuid();

        var result = await BulkAsync(workspace, token, [pending, registered, foreign, pending, missing], Registered);

        // Each device is reported once; a missing device and another workspace's read the same.
        result.Updated.Should().Equal(pending, registered);
        result.Failed.Select(f => (f.ClientRefId, f.Code)).Should().Equal(
            (foreign, "CLIENT_NOT_FOUND"),
            (missing, "CLIENT_NOT_FOUND"));
        (await StatusOfAsync(workspace, pending, token)).Should().Be(Registered);
        (await StatusOfAsync(strangerWorkspace, foreign, strangerToken)).Should().Be(Pending);
    }

    [SkippableFact]
    public async Task DEV_BULK_06_RevokingABatch_StopsEveryDeviceSigningIn()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (first, firstSecret) = await fixture.RegisterClientAsync(pin, "registered-1");
        var (second, secondSecret) = await fixture.RegisterClientAsync(pin, "registered-2");

        var result = await BulkAsync(workspace, token, [first, second], Revoked);

        result.Updated.Should().Equal(first, second);
        result.Failed.Should().BeEmpty();
        (await ClientLoginAsync(first, firstSecret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ClientLoginAsync(second, secondSecret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private async Task<string> CreateLimitedPolicyAsync(string token, Guid workspace, int maxClients)
    {
        var created = await Send(HttpMethod.Post, workspace, "registration-policies", token,
            new { Name = $"e2e-limit-{Guid.NewGuid():N}", MaxClients = maxClients, AutoApproval = false });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<PolicyDto>(JsonOptions))!.Pin;
    }

    private Task<HttpResponseMessage> SetStatusAsync(Guid workspace, Guid clientRef, string token, int status) =>
        Send(HttpMethod.Patch, workspace, $"clients/{clientRef}/status", token, new { Status = status });

    private async Task<BulkDto> BulkAsync(Guid workspace, string token, Guid[] clientRefs, int status)
    {
        var response = await Send(HttpMethod.Patch, workspace, "clients/status", token, new { ClientRefIds = clientRefs, Status = status });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BulkDto>(JsonOptions))!;
    }

    private async Task<JsonElement> DetailAsync(Guid workspace, Guid clientRef, string token)
    {
        var response = await Send(HttpMethod.Get, workspace, $"clients/{clientRef}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private async Task<int> StatusOfAsync(Guid workspace, Guid clientRef, string token) =>
        (await DetailAsync(workspace, clientRef, token)).GetProperty("status").GetInt32();

    private Task<HttpResponseMessage> Send(HttpMethod method, Guid workspace, string path, string token, object? body = null) =>
        fixture.SendAsync(method, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}"), token, body);

    private Task<HttpResponseMessage> ClientLoginAsync(Guid clientRef, string secret) =>
        fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl("/api/client-auth/login"), null, new { ClientRefId = clientRef, Secret = secret });
}
