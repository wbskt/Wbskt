using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// What happens to people reaches the audit log of the workspaces they belong to: a wrong password
/// on a member's account and an invitation sent from the tenant, with the caller's address on the
/// entry and the workspace list kept out of the stored event.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class EventLogPeopleTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task EVL_PEOPLE_01_AFailedSignInAndAnInvitation_AreLoggedInTheWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspace = await fixture.CreateWorkspaceAsync(user.Token);
        var tenant = await fixture.GetTenantRefAsync(user.Token);

        (await fixture.LoginRawAsync(user.Email, "not the password at all")).IsSuccessStatusCode.Should().BeFalse();
        await fixture.InviteAsync(user.Token, tenant, $"e2e-{Guid.NewGuid():N}"[..12] + "@test.local");

        List<JsonElement> entries = [];
        var logged = await ServicesFixture.PollAsync(async () =>
        {
            var response = await fixture.SendAsync(HttpMethod.Get, Url(workspace, "event-logs?eventNames=UserLoginFailedEvent,InvitationSentEvent&limit=50"), user.Token);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            entries = body.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
            var names = entries.Select(e => e.GetProperty("eventName").GetString()).ToHashSet();
            return names.Contains("UserLoginFailedEvent") && names.Contains("InvitationSentEvent");
        }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
        logged.Should().BeTrue("both reach the log of the workspace the member belongs to");

        var failed = entries.First(e => e.GetProperty("eventName").GetString() == "UserLoginFailedEvent");
        failed.GetProperty("clientAddress").GetString().Should().NotBeNullOrEmpty("the address the attempt came from is on the entry");
        using (var data = JsonDocument.Parse(failed.GetProperty("eventData").GetString()!))
        {
            data.RootElement.TryGetProperty("ipAddress", out _).Should().BeFalse("the address is kept on the entry, not in the stored event");
            data.RootElement.TryGetProperty("workspaceIds", out _).Should().BeFalse("which workspaces an event was logged in is not part of it");
        }

        var invited = entries.First(e => e.GetProperty("eventName").GetString() == "InvitationSentEvent");
        invited.GetProperty("source").GetString().Should().Be("Api");

        (await NamesAsync(workspace, "event-logs?group=security&limit=200", user.Token)).Should().Contain("UserLoginFailedEvent");
        (await NamesAsync(workspace, "event-logs?group=people&limit=200", user.Token)).Should().Contain("InvitationSentEvent");
    }

    private async Task<List<string>> NamesAsync(Guid workspace, string path, string token)
    {
        var response = await fixture.SendAsync(HttpMethod.Get, Url(workspace, path), token);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("eventName").GetString()!).ToList();
    }

    private static string Url(Guid workspace, string path) => ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}");
}
