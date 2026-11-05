using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Protocol;

namespace Wbskt.E2E.FeatureTests.Scenarios.Clients;

/// <summary>
/// Firmware updates from the server side: <c>POST clients/{ref}/ota</c> goes only to a client whose
/// capabilities list <c>ota</c>, reaches it as <c>sys.ota.update</c> with its payload as an object, and
/// the client's <c>sys.ota.status</c> lands in its activity. The client is a <see cref="RawClientSocket"/>,
/// so nothing here depends on an SDK taking updates.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(ClientsCollection.Name)]
public sealed class ClientOtaE2ETests(ServicesFixture fixture)
{
    private const string Digest = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
    private const string FirmwareUrl = "https://firmware.example/e2e-1.1.0.bin";

    [SkippableFact]
    public async Task DEV_OTA_01_AnUpdate_ReachesAClientThatTakesThem_AndItsStatusIsInItsActivity()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, $"e2e-ota-{Guid.NewGuid():N}");
        await using var client = await RawClientSocket.ConnectAsync(await fixture.LoginClientAsync(clientRef, secret));
        await client.SendAsync(new ProtocolMessage(MessageTypes.Capabilities, new { agent = "e2e-raw", version = "1.0.0", os = "test", capabilities = Array.Empty<object>(), system = new[] { "ota" } }));
        await WaitForSystemAsync(token, workspace, clientRef);

        var badUrl = await fixture.SendAsync(HttpMethod.Post, Ota(workspace, clientRef), token, new { url = "http://firmware.example/a.bin", sha256 = Digest });
        badUrl.StatusCode.Should().Be(HttpStatusCode.BadRequest, "firmware is only fetched over https");
        var badDigest = await fixture.SendAsync(HttpMethod.Post, Ota(workspace, clientRef), token, new { url = FirmwareUrl, sha256 = "abc" });
        badDigest.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the digest is 64 hex digits");

        var response = await fixture.SendAsync(HttpMethod.Post, Ota(workspace, clientRef), token, new { url = FirmwareUrl, sha256 = Digest, version = "1.1.0" });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var commandId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("commandId").GetString();

        // Presence reaches the management host over the bus, so the update may have been queued and
        // delivered on this connection either way; the socket host's pings may come in between. A
        // read that times out aborts the socket, so it waits once, for the whole budget.
        var update = await NextOfAsync(client, MessageTypes.OtaUpdate, TimeSpan.FromSeconds(30));

        update.GetProperty("commandId").GetString().Should().Be(commandId);
        update.TryGetProperty("expiresAt", out _).Should().BeTrue("an update always expires");
        var payload = update.GetProperty("payload");
        payload.ValueKind.Should().Be(JsonValueKind.Object, "the client reads fields from it");
        payload.GetProperty("url").GetString().Should().Be(FirmwareUrl);
        payload.GetProperty("sha256").GetString().Should().Be(Digest);

        await client.SendAsync(new ProtocolMessage(MessageTypes.Ack, new AckPayload(commandId!)));
        await client.SendAsync(new ProtocolMessage(MessageTypes.OtaStatus, new OtaStatusPayload(commandId, OtaStates.Accepted, Version: "1.1.0")));

        await ServicesFixture.PollUntilAsync(
            "ClientOtaStatusEvent in the client's activity",
            async () =>
            {
                var logs = await fixture.GetEventLogsAsync(token, workspace, "ClientOtaStatusEvent");
                var mine = logs.Where(l => l.ClientRefId == clientRef).ToList();
                return (mine.Any(l => l.EventData.Contains(OtaStates.Accepted, StringComparison.Ordinal)), $"{mine.Count} entries");
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task DEV_OTA_02_AClientThatDoesNotListOta_IsRefusedAnUpdate()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, $"e2e-ota-none-{Guid.NewGuid():N}");

        var response = await fixture.SendAsync(HttpMethod.Post, Ota(workspace, clientRef), token, new { url = FirmwareUrl, sha256 = Digest });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("OTA_NOT_SUPPORTED");

        var generic = await fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl($"/api/v1/workspaces/{workspace}/clients/{clientRef}/commands"), token,
            new { type = MessageTypes.OtaUpdate, payload = "{}" });
        generic.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the generic command path still refuses reserved types");
    }

    private static async Task<JsonElement> NextOfAsync(RawClientSocket client, string type, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var seen = new List<string>();
        while (DateTime.UtcNow < deadline)
        {
            var read = await client.ReceiveAsync(deadline - DateTime.UtcNow);
            if (read?.Text is not { } text)
            {
                break;
            }

            var frame = JsonDocument.Parse(text).RootElement;
            if (frame.GetProperty("type").GetString() == type)
            {
                return frame.Clone();
            }

            seen.Add(text);
        }

        throw new Xunit.Sdk.XunitException($"No '{type}' within {timeout}; saw {string.Join(", ", seen)}");
    }

    private Task WaitForSystemAsync(string token, Guid workspace, Guid clientRef) =>
        ServicesFixture.PollUntilAsync(
            $"client {clientRef} to list ota",
            async () =>
            {
                var detail = await fixture.SendAsync(HttpMethod.Get, ServicesFixture.ManagementUrl($"/api/v1/workspaces/{workspace}/clients/{clientRef}"), token);
                var body = await detail.Content.ReadAsStringAsync();
                var listed = detail.IsSuccessStatusCode
                    && JsonDocument.Parse(body).RootElement.GetProperty("system").EnumerateArray().Any(s => s.GetString() == SystemCapabilities.Ota);
                return (listed, body);
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(1));

    private static string Ota(Guid workspace, Guid clientRef) =>
        ServicesFixture.ManagementUrl($"/api/v1/workspaces/{workspace}/clients/{clientRef}/ota");
}
