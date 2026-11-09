using System.Text.Json;
using FluentAssertions;
using Wbskt;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Clients;

/// <summary>
/// <c>GET clients/{ref}/message-types</c> lists what a client has sent, with counts and the latest
/// payload, and marks the types the platform reads itself.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(ClientsCollection.Name)]
public sealed class ClientMessageTypeE2ETests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task DEV_TYPES_01_TheTypesAClientSent_AreListedWithCountsAndTheLatestPayload()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var clientName = $"e2e-types-{Guid.NewGuid():N}";
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, clientName);

        await using var client = new WbsktClient(
            new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, clientName, null),
            new InMemoryClientStorage(clientRef, secret));
        await client.StartAsync();
        await client.SendAsync("e2e.reading", new { c = 20 });
        await client.SendAsync("e2e.reading", new { c = 21 });
        await client.SendAsync("e2e.boot", new { });

        JsonElement listed = default;
        await ServicesFixture.PollUntilAsync(
            "both types in the client's message types",
            async () =>
            {
                var response = await fixture.SendAsync(HttpMethod.Get,
                    ServicesFixture.ManagementUrl($"/api/v1/workspaces/{workspace}/clients/{clientRef}/message-types"), token);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return (false, body);
                }

                listed = JsonDocument.Parse(body).RootElement.GetProperty("items").Clone();
                var reading = Find(listed, "e2e.reading");
                return (reading is { } r && r.GetProperty("count").GetInt64() == 2 && Find(listed, "e2e.boot") is not null, body);
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(1));

        var readingType = Find(listed, "e2e.reading")!.Value;
        readingType.GetProperty("isPlatform").GetBoolean().Should().BeFalse();
        readingType.GetProperty("samplePayload").GetProperty("c").GetInt32().Should().Be(21, "the sample is the latest payload");
        Find(listed, "capabilities")?.GetProperty("isPlatform").GetBoolean().Should().BeTrue("the SDK's capabilities are the platform's own type");
    }

    private static JsonElement? Find(JsonElement items, string type) =>
        items.EnumerateArray().Where(i => i.GetProperty("type").GetString() == type).Select(i => (JsonElement?)i).FirstOrDefault();
}
