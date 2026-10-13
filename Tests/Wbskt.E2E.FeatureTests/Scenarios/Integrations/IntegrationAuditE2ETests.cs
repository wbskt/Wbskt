using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Integrations;

/// <summary>
/// The event log's record of integrations: each change is logged with who made it and what changed,
/// replacing or deleting a credential reads as a security event, and no entry ever carries a secret.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class IntegrationAuditE2ETests(ServicesFixture fixture)
{
    private readonly IntegrationsApi _api = new(fixture);

    [SkippableFact]
    public async Task INT_AUDIT_01_EveryChange_IsLogged_WithoutTheSecret()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var api = FakeHttpServer.Start();
        var (token, ws) = await _api.NewWorkspaceAsync();
        const string firstToken = "audit-first-token-1111";
        const string secondToken = "audit-second-token-2222";

        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("Audited", api.BaseUrl, token: firstToken)));
        await _api.UpdateAsync(token, ws, refId, new { name = "Audited v2", config = new { baseUrl = api.BaseUrl + "/v2", mode = "bearer" } });
        await _api.UpdateAsync(token, ws, refId, new { secrets = new { token = secondToken } });
        await _api.TestAsync(token, ws, new { refId });
        (await _api.DeleteRawAsync(token, ws, refId)).EnsureSuccessStatusCode();

        string[] expected =
        [
            "IntegrationCreatedEvent", "IntegrationUpdatedEvent", "IntegrationSecretRotatedEvent",
            "IntegrationTestedEvent", "IntegrationDeletedEvent"
        ];
        IReadOnlyList<EventLogItemDto> logs = [];
        (await ServicesFixture.PollAsync(
                async () =>
                {
                    logs = (await fixture.GetEventLogsAsync(token, ws)).Where(l => l.EventName.StartsWith("Integration", StringComparison.Ordinal)).ToList();
                    return expected.All(name => logs.Any(l => l.EventName == name));
                },
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(500)))
            .Should().BeTrue($"each change is logged; found {string.Join(", ", logs.Select(l => l.EventName))}");

        logs.Should().OnlyContain(l => !l.EventData.Contains(firstToken) && !l.EventData.Contains(secondToken), "no entry carries a secret");
        logs.Should().OnlyContain(l => l.UserRefId != null, "each entry names who acted");

        var updated = Data(logs, "IntegrationUpdatedEvent");
        var changed = updated.Prop("Changes").EnumerateArray().Select(c => c.Prop("Field").GetString()).ToList();
        changed.Should().Contain(["name", "config.baseUrl"]);

        var rotated = logs.Single(l => l.EventName == "IntegrationSecretRotatedEvent");
        rotated.Criticality.Should().Be(1, "replacing a credential is a warning");
        Data(logs, "IntegrationSecretRotatedEvent").Prop("Changes").EnumerateArray()
            .Select(c => c.Prop("Field").GetString()).Should().Equal("secretHint");

        logs.Single(l => l.EventName == "IntegrationDeletedEvent").Criticality.Should().Be(1);
        Data(logs, "IntegrationTestedEvent").Prop("Ok").GetBoolean().Should().BeTrue();
    }

    [SkippableFact]
    public async Task INT_AUDIT_02_TheIntegrationsGroup_AndTheSecurityGroup_FindThem()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Grouped")));
        await _api.UpdateAsync(token, ws, refId, new { secrets = new { botToken = "555555:CCG-grouped-e2e-bot-token-5555" } });

        async Task<IReadOnlyList<string>> GroupAsync(string group)
        {
            using var response = await fixture.SendAsync(
                HttpMethod.Get,
                ServicesFixture.ManagementUrl($"/api/workspaces/{ws}/event-logs?group={group}&limit=100"),
                token);
            response.EnsureSuccessStatusCode();
            var page = await IntegrationsApi.ReadAsync(response);
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("eventName").GetString()!).ToList();
        }

        IReadOnlyList<string> integrations = [];
        (await ServicesFixture.PollAsync(
                async () =>
                {
                    // Each change is logged from its own message, so the rotation can land before the
                    // creation: wait for both rather than assume the order they were made in.
                    integrations = await GroupAsync("integrations");
                    return integrations.Contains("IntegrationSecretRotatedEvent") && integrations.Contains("IntegrationCreatedEvent");
                },
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(500)))
            .Should().BeTrue();
        integrations.Should().Contain("IntegrationCreatedEvent").And.OnlyContain(n => n.StartsWith("Integration"));

        var security = await GroupAsync("security");
        security.Should().Contain("IntegrationSecretRotatedEvent").And.NotContain("IntegrationCreatedEvent");
    }

    /// <summary>The logged payload. Its property casing is the log's business, so lookups ignore case.</summary>
    private static JsonElement Data(IEnumerable<EventLogItemDto> logs, string eventName) =>
        JsonDocument.Parse(logs.First(l => l.EventName == eventName).EventData).RootElement;
}

internal static class JsonElementCaseless
{
    public static JsonElement Prop(this JsonElement element, string name) =>
        element.EnumerateObject().First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
