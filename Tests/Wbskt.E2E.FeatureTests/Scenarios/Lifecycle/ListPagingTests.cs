using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// Every list endpoint answers with the same page shape (<c>items</c> and <c>nextCursor</c>) and
/// takes the same <c>cursor</c> and <c>limit</c>, so a caller reads any of them by following
/// <c>nextCursor</c> until it is null. Each list here holds more than one page, and the walk must
/// see every item exactly once.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class ListPagingTests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task LIST_PAGE_01_EveryList_FollowsItsCursorPastOnePage()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();

        var policies = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            policies.Add((await fixture.CreatePolicyAsync(token, workspace)).PolicyRef);
        }

        var (clientPolicy, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var clients = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            clients.Add((await fixture.RegisterClientAsync(pin, $"paging-{i}")).ClientRefId);
        }

        var templates = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var created = await fixture.SendAsync(
                HttpMethod.Post,
                ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/message-templates"),
                token,
                new { Name = $"paging-{i}", MessageType = "paging.test", PayloadJson = "{}", PolicyRefId = (Guid?)null });
            created.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            templates.Add(body.RootElement.GetProperty("refId").GetGuid());
        }

        var workflows = new List<Guid>();
        var triggers = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var refId = Guid.NewGuid();
            var definition = new WorkflowBuilder($"E2E-Paging-{refId:N}", refId)
                .AddManualTrigger(out var triggerNodeId)
                .BuildAndValidate();
            workflows.Add(await fixture.PublishWorkflowAsync(token, workspace, refId, $"E2E-Paging-{refId:N}", JsonSerializer.SerializeToElement(definition, JsonOpts)));
            triggers.Add(triggerNodeId);
        }

        // Three runs of the first workflow, so its own run list holds more than one page too.
        for (var i = 0; i < 3; i++)
        {
            await fixture.StartManualRunAsync(token, workspace, workflows[0], triggers[0].ToString(), idempotencyKey: null);
        }

        var runs = new List<Guid>();
        await ServicesFixture.PollAsync(async () =>
        {
            runs = (await fixture.ListRunsAsync(token, workspace, workflows[0])).Select(r => r.RefId).ToList();
            return runs.Count >= 3;
        }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
        runs.Should().HaveCount(3, "the three manual starts each made a run");

        (await WalkAsync(token, workspace, "registration-policies", "refId")).Should().BeEquivalentTo(policies.Append(clientPolicy).Select(p => p.ToString()));
        (await WalkAsync(token, workspace, "clients", "clientRefId")).Should().BeEquivalentTo(Keys(clients));
        (await WalkAsync(token, workspace, $"clients/policy/{clientPolicy}", "clientRefId")).Should().BeEquivalentTo(Keys(clients));
        (await WalkAsync(token, workspace, "message-templates", "refId")).Should().BeEquivalentTo(Keys(templates));
        (await WalkAsync(token, workspace, "workflows", "refId")).Should().BeEquivalentTo(Keys(workflows));
        (await WalkAsync(token, workspace, $"workflows/{workflows[0]}/runs", "refId")).Should().BeEquivalentTo(Keys(runs));
        (await WalkAsync(token, workspace, "runs", "refId")).Should().BeEquivalentTo(Keys(runs));

        // A run's history is only as long as the run made it, so walk it one event at a time and
        // check the walk against the whole history read as a single page, once the run is done
        // adding to it.
        (await fixture.WaitForRunTerminalAsync(token, workspace, runs[0], TimeSpan.FromSeconds(30))).Should().NotBeNull("a trigger-only run finishes at once");
        var history = await ReadPageAsync(token, workspace, $"runs/{runs[0]}/history?limit=200");
        history.NextCursor.Should().BeNull("the whole history fits one page of 200");
        var historyIds = history.Items.Select(e => e.GetProperty("historyEventId").GetInt64().ToString()).ToList();
        historyIds.Should().HaveCountGreaterThan(1, "a finished run records more than one event");
        (await WalkAsync(token, workspace, $"runs/{runs[0]}/history", "historyEventId", limit: 1)).Should().Equal(historyIds);
    }

    [SkippableFact]
    public async Task LIST_PAGE_02_ACursorFromNowhere_IsABadRequest()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();

        foreach (var list in new[] { "clients", "registration-policies", "message-templates", "workflows", "event-logs", "runs" })
        {
            var response = await fixture.SendAsync(HttpMethod.Get, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{list}?cursor=not-a-cursor"), token);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, list);
            (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("PAGE_CURSOR_INVALID", list);
        }
    }

    /// <summary>
    /// Reads a list <paramref name="limit"/> items at a time by following <c>nextCursor</c>, and
    /// returns the <paramref name="key"/> of every item in the order the pages gave them.
    /// </summary>
    private async Task<List<string>> WalkAsync(string token, Guid workspace, string list, string key, int limit = 2)
    {
        var keys = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var separator = list.Contains('?') ? '&' : '?';
            var page = await ReadPageAsync(token, workspace, $"{list}{separator}limit={limit}{(cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}")}");
            page.Items.Should().HaveCountLessThanOrEqualTo(limit, list);
            keys.AddRange(page.Items.Select(item => item.GetProperty(key).ToString()));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 50);

        cursor.Should().BeNull($"the walk of {list} reached its last page");
        pages.Should().BeGreaterThan(1, $"{list} holds more than one page of {limit}");
        keys.Should().OnlyHaveUniqueItems($"no item of {list} is on two pages");
        return keys;
    }

    private async Task<(List<JsonElement> Items, string? NextCursor)> ReadPageAsync(string token, Guid workspace, string pathAndQuery)
    {
        var response = await fixture.SendAsync(HttpMethod.Get, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{pathAndQuery}"), token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, pathAndQuery);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
        var next = body.RootElement.GetProperty("nextCursor");
        return (items, next.ValueKind == JsonValueKind.Null ? null : next.GetString());
    }

    private static IEnumerable<string> Keys(IEnumerable<Guid> refIds) => refIds.Select(refId => refId.ToString());
}
