using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// The devices host: a management host running as Host:Role Devices, which in production takes
/// device registration and login off the console API's container (Traefik sends it those paths on
/// the same domain). These pin that a device can do everything it needs through it alone, that it
/// shares the console's view of every device, and that it serves nothing else.
///
/// Requires the hosts from start-hosts.sh, including "devices". Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DeviceHostTests(ServicesFixture fixture)
{
    private const int Pending = 0;
    private const int Registered = 1;
    private const int Revoked = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record RegistrationDto(Guid ClientRefId, string Secret, int Status);

    private sealed record LoginDto(string AccessToken, int ExpiresIn);

    [SkippableFact]
    public async Task DEV_HOST_01_TheSdk_RegistersSignsInAndConnects_ThroughTheDevicesHostAlone()
    {
        SkipUnlessHostsUp();

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var storage = new InMemoryClientStorage();
        var config = new ClientConfig(E2EConfig.DevicesBaseUrl, E2EConfig.SocketWsBaseUrl, $"devices-host-{Guid.NewGuid():N}", pin);
        await using var client = new WbsktClient(config, storage);

        await client.StartAsync();

        var (clientRef, _) = await storage.LoadCredentialsAsync();
        clientRef.Should().NotBeNull("the SDK registered through the devices host and stored what it got back");
        var detail = await WaitForAsync(workspace, clientRef!.Value, token, d => d.GetProperty("isConnected").GetBoolean());
        detail.GetProperty("status").GetInt32().Should().Be(Registered);
    }

    [SkippableFact]
    public async Task DEV_HOST_02_ADeviceRegisteredThroughTheDevicesHost_WaitsForTheConsole_ThenSignsIn()
    {
        SkipUnlessHostsUp();

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: false);

        var registered = await DevicesAsync("/api/client-registrations/initiate", new { Pin = pin, Name = "via-devices-host" });
        registered.StatusCode.Should().Be(HttpStatusCode.OK);
        var device = (await registered.Content.ReadFromJsonAsync<RegistrationDto>(JsonOptions))!;
        device.Status.Should().Be(Pending);

        (await DevicesLoginAsync(device.ClientRefId, device.Secret)).IsSuccessStatusCode.Should().BeFalse("a pending device cannot sign in");
        (await DetailAsync(workspace, device.ClientRefId, token)).GetProperty("status").GetInt32().Should().Be(Pending);

        (await ManagementAsync(HttpMethod.Patch, workspace, $"clients/{device.ClientRefId}/status", token, new { Status = Registered }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var login = await DevicesLoginAsync(device.ClientRefId, device.Secret);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        (await login.Content.ReadFromJsonAsync<LoginDto>(JsonOptions))!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task DEV_HOST_03_ADeviceRevokedInTheConsole_CannotSignInThroughTheDevicesHost()
    {
        SkipUnlessHostsUp();

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, "to-revoke");
        (await DevicesLoginAsync(clientRef, secret)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ManagementAsync(HttpMethod.Patch, workspace, $"clients/{clientRef}/status", token, new { Status = Revoked }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var login = await DevicesLoginAsync(clientRef, secret);
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task DEV_HOST_04_BadCredentialsAndAnUnknownPin_AreRefused_ExactlyAsTheConsoleHostRefusesThem()
    {
        SkipUnlessHostsUp();

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "wrong-secret");

        var wrongSecret = await DevicesLoginAsync(clientRef, "not-the-secret");
        wrongSecret.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ServicesFixture.ReadErrorCodeAsync(wrongSecret)).Should().Be("CLIENT_UNAUTHORIZED");

        var unknownPin = new { Pin = "ZZZZZZZZZZZZ", Name = "no-policy" };
        var viaDevices = await DevicesAsync("/api/client-registrations/initiate", unknownPin);
        var viaConsole = await fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl("/api/client-registrations/initiate"), body: unknownPin);
        viaDevices.IsSuccessStatusCode.Should().BeFalse();
        viaDevices.StatusCode.Should().Be(viaConsole.StatusCode);
        (await ServicesFixture.ReadErrorCodeAsync(viaDevices)).Should().Be(await ServicesFixture.ReadErrorCodeAsync(viaConsole));
    }

    [SkippableFact]
    public async Task DEV_HOST_05_TheDevicesHost_ServesNothingButDeviceRegistrationAndLogin()
    {
        SkipUnlessHostsUp();

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (policyRef, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "console-only");

        var consoleOnly = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, $"/api/workspaces/{workspace}/clients"),
            (HttpMethod.Get, $"/api/workspaces/{workspace}/clients/{clientRef}"),
            (HttpMethod.Patch, $"/api/workspaces/{workspace}/clients/{clientRef}/status"),
            (HttpMethod.Get, $"/api/workspaces/{workspace}/registration-policies/{policyRef}"),
            (HttpMethod.Get, $"/api/workspaces/{workspace}/workflows"),
            (HttpMethod.Post, "/hubs/notifications/negotiate?negotiateVersion=1")
        };

        foreach (var (method, path) in consoleOnly)
        {
            var response = await fixture.SendAsync(method, $"{E2EConfig.DevicesBaseUrl}{path}", token, method == HttpMethod.Patch ? new { Status = Revoked } : null);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{method} {path} is the console's, not the devices host's");
        }

        (await DetailAsync(workspace, clientRef, token)).GetProperty("status").GetInt32().Should().Be(Registered);
    }

    [SkippableFact]
    public async Task DEV_HOST_06_TheDevicesHost_IsReady_AndPublishesTheSameSigningKeysAsTheConsoleHost()
    {
        SkipUnlessHostsUp();

        (await fixture.SendAsync(HttpMethod.Get, $"{E2EConfig.DevicesBaseUrl}/healthz/ready")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The socket host checks device tokens against these keys, so a token from either host must verify.
        var devicesKeys = await KeyIdsAsync(E2EConfig.DevicesBaseUrl);
        devicesKeys.Should().NotBeEmpty();
        devicesKeys.Should().BeEquivalentTo(await KeyIdsAsync(E2EConfig.ManagementBaseUrl));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private void SkipUnlessHostsUp()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        Skip.IfNot(fixture.DevicesHostAvailable, "The devices host is not running (start-hosts.sh starts it) — skipping.");
    }

    private Task<HttpResponseMessage> DevicesAsync(string path, object body) =>
        fixture.SendAsync(HttpMethod.Post, $"{E2EConfig.DevicesBaseUrl}{path}", body: body);

    private Task<HttpResponseMessage> DevicesLoginAsync(Guid clientRef, string secret) =>
        DevicesAsync("/api/client-auth/login", new { ClientRefId = clientRef, Secret = secret });

    private Task<HttpResponseMessage> ManagementAsync(HttpMethod method, Guid workspace, string path, string token, object? body = null) =>
        fixture.SendAsync(method, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}"), token, body);

    private async Task<JsonElement> DetailAsync(Guid workspace, Guid clientRef, string token)
    {
        var response = await ManagementAsync(HttpMethod.Get, workspace, $"clients/{clientRef}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    // Presence is recorded from the socket host's events, so it lands shortly after the connect.
    private async Task<JsonElement> WaitForAsync(Guid workspace, Guid clientRef, string token, Func<JsonElement, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var detail = await DetailAsync(workspace, clientRef, token);
            if (condition(detail) || DateTime.UtcNow > deadline)
            {
                condition(detail).Should().BeTrue($"the console should see the device's change in time; last detail: {detail}");
                return detail;
            }

            await Task.Delay(250);
        }
    }

    private async Task<List<string>> KeyIdsAsync(string baseUrl)
    {
        var response = await fixture.SendAsync(HttpMethod.Get, $"{baseUrl}/.well-known/jwks.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("keys").EnumerateArray().Select(k => k.GetProperty("kid").GetString()!).ToList();
    }
}
