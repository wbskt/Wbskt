using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// The audit log's reads of <c>GET event-logs</c>: groups, device traffic, text search, time range,
/// the per-group summary and the CSV export, each against what a real workspace logged.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class EventLogFilterTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task EVL_FILTER_01_GroupsTrafficSearchSummaryAndCsv_ReadWhatWasLogged()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var name = $"audit-{Guid.NewGuid():N}";
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, name);
        (await Send(workspace, $"clients/{clientRef}", token, HttpMethod.Patch, new { Name = name + "-renamed" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A message from the device is device traffic, which the audit views leave out.
        var storage = new InMemoryClientStorage(clientRef, secret);
        await using (var device = new WbsktClient(new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, name, null), storage))
        {
            await device.StartAsync();
            await device.SendAsync("telemetry", new { value = 1 });

            var logged = await ServicesFixture.PollAsync(async () =>
            {
                var names = (await ItemsAsync(workspace, "event-logs?limit=200", token)).Select(Event).ToList();
                return names.Contains("ClientRenamedEvent") && names.Contains("ClientMessageReceivedEvent") && names.Contains("PolicyCreatedEvent");
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
            logged.Should().BeTrue("the policy, the rename and the device's message all reach the log");
        }

        var all = await ItemsAsync(workspace, "event-logs?limit=200", token);
        all.Should().OnlyContain(e => e.GetProperty("id").GetInt64() > 0, "every entry names its own id");

        var policies = await ItemsAsync(workspace, "event-logs?group=policies&limit=200", token);
        policies.Select(Event).Should().Contain("PolicyCreatedEvent").And.OnlyContain(n => n.StartsWith("Policy"));

        var withoutTraffic = await ItemsAsync(workspace, "event-logs?traffic=exclude&limit=200", token);
        withoutTraffic.Select(Event).Should().NotContain("ClientMessageReceivedEvent").And.Contain("ClientRenamedEvent");

        var named = await ItemsAsync(workspace, $"event-logs?eventNames=ClientRenamedEvent,PolicyCreatedEvent&limit=200", token);
        named.Select(Event).Distinct().Should().BeEquivalentTo(["ClientRenamedEvent", "PolicyCreatedEvent"]);

        var found = await ItemsAsync(workspace, $"event-logs?q={name}-renamed&limit=200", token);
        found.Select(Event).Should().Contain("ClientRenamedEvent");
        found.Should().OnlyContain(e => e.GetProperty("eventData").GetString()!.Contains(name + "-renamed", StringComparison.OrdinalIgnoreCase));

        // A window that has not happened yet holds nothing; a from after the default to (now) is refused.
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(2).ToString("O"));
        (await ItemsAsync(workspace, $"event-logs?from={from}&to={to}", token)).Should().BeEmpty();
        var inverted = await Send(workspace, $"event-logs?from={from}", token);
        inverted.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(inverted)).Should().Be("TIME_RANGE_INVALID");

        var fromApi = await ItemsAsync(workspace, "event-logs?source=Api&limit=200", token);
        fromApi.Select(Event).Should().Contain("ClientRenamedEvent");
        fromApi.Should().OnlyContain(e => e.GetProperty("source").GetString() == "Api");

        var warnings = await ItemsAsync(workspace, "event-logs?minCriticality=Warning&limit=200", token);
        warnings.Should().OnlyContain(e => e.GetProperty("criticality").GetRawText() != "\"Info\"" && e.GetProperty("criticality").GetRawText() != "0");

        // A live view asks for what is newer than the newest entry it shows.
        var since = all[1].GetProperty("id").GetInt64();
        var newer = await ItemsAsync(workspace, $"event-logs?sinceId={since}&limit=200", token);
        newer.Select(e => e.GetProperty("id").GetInt64()).Should().Contain(all[0].GetProperty("id").GetInt64()).And.OnlyContain(id => id > since);

        var badSource = await Send(workspace, "event-logs?source=Robot", token);
        badSource.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(badSource)).Should().Be("EVENT_LOG_SOURCE_UNKNOWN");

        var unknown = await Send(workspace, "event-logs?group=billing", token);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(unknown)).Should().Be("EVENT_LOG_GROUP_UNKNOWN");

        var summaryResponse = await Send(workspace, "event-logs/summary?traffic=exclude", token);
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var summary = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync()))
        {
            var groups = summary.RootElement.GetProperty("groups");
            groups.GetProperty("policies").GetInt64().Should().BeGreaterThanOrEqualTo(1);
            groups.GetProperty("clients").GetInt64().Should().BeGreaterThanOrEqualTo(1);
            summary.RootElement.GetProperty("events").GetProperty("ClientRenamedEvent").GetInt64().Should().BeGreaterThanOrEqualTo(1);
            summary.RootElement.GetProperty("sources").GetProperty("Api").GetInt64().Should().BeGreaterThanOrEqualTo(1);
            summary.RootElement.GetProperty("people").EnumerateObject().Should().NotBeEmpty("the rename was made by a signed-in user");
            // At least: the device's disconnect can land after the list above was read.
            summary.RootElement.GetProperty("total").GetInt64().Should().BeGreaterThanOrEqualTo(withoutTraffic.Count, "the window holds every entry this new workspace has");
        }

        var csv = await Send(workspace, "event-logs/csv?group=clients", token);
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var lines = (await csv.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("id,createdAt,event,criticality,userRefId,clientRefId,policyRefId,workflowRefId,data");
        lines.Should().Contain(l => l.Contains(",ClientRenamedEvent,"));
    }

    private static string Event(JsonElement entry) => entry.GetProperty("eventName").GetString()!;

    private async Task<List<JsonElement>> ItemsAsync(Guid workspace, string path, string token)
    {
        var response = await Send(workspace, path, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private Task<HttpResponseMessage> Send(Guid workspace, string path, string token, HttpMethod? method = null, object? body = null) =>
        fixture.SendAsync(method ?? HttpMethod.Get, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}"), token, body);
}
