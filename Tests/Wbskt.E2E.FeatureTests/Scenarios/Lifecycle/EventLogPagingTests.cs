using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// The event log pages by cursor, newest first, and every list endpoint clamps the page size it is
/// asked for instead of passing it to SQL.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class EventLogPagingTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task EVL_PAGE_01_TheEventLog_PagesByCursorWithoutRepeats()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        for (var i = 0; i < 3; i++)
        {
            await fixture.RegisterClientAsync(pin, $"paging-{i}");
        }

        // Entries reach the log through the bus and a batched flush, so wait for the whole set.
        var all = Array.Empty<string>();
        await ServicesFixture.PollAsync(async () =>
        {
            all = (await PageAsync(token, workspace, take: 200, cursor: null)).Entries;
            return all.Length >= 3;
        }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
        all.Length.Should().BeGreaterThanOrEqualTo(3, "the three registrations were logged");

        // Walk it two at a time.
        var walked = new List<string>();
        string? cursor = null;
        for (var pages = 0; pages < 50; pages++)
        {
            var page = await PageAsync(token, workspace, take: 2, cursor);
            page.Entries.Length.Should().BeLessThanOrEqualTo(2);
            walked.AddRange(page.Entries);
            cursor = page.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        cursor.Should().BeNull("the walk reached the last page");
        // Anything logged during the walk is newer, so it can only add to the front.
        walked.Should().OnlyHaveUniqueItems("no entry is on two pages");
        walked.Should().ContainInOrder(all, "the pages are the whole log, newest first");
        walked.Should().EndWith(all[^1], "the walk reaches the oldest entry");
    }

    // The parameters lists took before the paging contract still work for one release.
    [SkippableFact]
    public async Task EVL_PAGE_02_PageSizes_AreClampedNotPassedThrough()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();

        foreach (var query in new[] { "clients?skip=-5&take=2147483647", "registration-policies?take=-1", "event-logs?take=2147483647", "runs?top=2147483647", "clients?limit=2147483647", "runs?limit=-1" })
        {
            var response = await fixture.SendAsync(HttpMethod.Get, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{query}"), token);
            response.StatusCode.Should().Be(HttpStatusCode.OK, query);
        }
    }

    private async Task<(string[] Entries, string? NextCursor)> PageAsync(string token, Guid workspace, int take, string? cursor)
    {
        var url = ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/event-logs?limit={take}{(cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}")}");
        var response = await fixture.SendAsync(HttpMethod.Get, url, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("X-Total-Count").Should().BeFalse("the log no longer counts every row per page");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => $"{e.GetProperty("eventName").GetString()}|{e.GetProperty("eventData").GetString()}|{e.GetProperty("createdAtUtc").GetString()}")
            .ToArray();
        var next = body.RootElement.GetProperty("nextCursor");
        return (entries, next.ValueKind == JsonValueKind.Null ? null : next.GetString());
    }
}
