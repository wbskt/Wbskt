using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// An action taken through the API is logged with where it came from and what it changed: its
/// source, the caller's address on the entry (not inside the stored event), and the fields before
/// and after.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class EventLogSourceTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task EVL_SOURCE_01_ARename_RecordsItsSourceAddressAndChange()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "source-before");
        (await fixture.SendAsync(HttpMethod.Patch, Url(workspace, $"clients/{clientRef}"), token, new { Name = "source-after" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement renamed = default;
        var logged = await ServicesFixture.PollAsync(async () =>
        {
            var response = await fixture.SendAsync(HttpMethod.Get, Url(workspace, "event-logs?eventNames=ClientRenamedEvent"), token);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = body.RootElement.GetProperty("items");
            if (items.GetArrayLength() == 0)
            {
                return false;
            }

            renamed = items[0].Clone();
            return true;
        }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
        logged.Should().BeTrue("the rename reaches the log");

        // The test client sends no Origin header, so it is an API caller, not the console.
        renamed.GetProperty("source").GetString().Should().Be("Api");
        renamed.GetProperty("clientAddress").GetString().Should().NotBeNullOrEmpty();

        using var data = JsonDocument.Parse(renamed.GetProperty("eventData").GetString()!);
        data.RootElement.TryGetProperty("clientAddress", out _).Should().BeFalse("the address is kept on the entry, not in the stored event");
        var change = data.RootElement.GetProperty("changes").EnumerateArray().Should().ContainSingle().Subject;
        change.GetProperty("field").GetString().Should().Be("name");
        change.GetProperty("before").GetString().Should().Be("source-before");
        change.GetProperty("after").GetString().Should().Be("source-after");
    }

    private static string Url(Guid workspace, string path) => ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}");
}
