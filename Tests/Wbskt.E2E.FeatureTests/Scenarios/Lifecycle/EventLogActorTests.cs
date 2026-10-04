using System.Text;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// An action taken through the API is recorded in the event log with who took it.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class EventLogActorTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task EVL_ACTOR_01_APolicyCreatedThroughTheApi_RecordsWhoCreatedIt()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var userRef = UserRefFrom(token);
        var (policyRef, _) = await fixture.CreatePolicyAsync(token, workspace);

        // Entries reach the log through the bus and a batched flush.
        EventLogItemDto? created = null;
        await ServicesFixture.PollAsync(async () =>
        {
            var logs = await fixture.GetEventLogsAsync(token, workspace, "PolicyCreatedEvent");
            created = logs.FirstOrDefault(e => e.PolicyRefId == policyRef);
            return created is not null;
        }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));

        created.Should().NotBeNull("creating a policy is logged");
        created!.UserRefId.Should().Be(userRef, "the log records who created the policy");
    }

    /// <summary>The user's reference, from the access token's <c>uref</c> claim.</summary>
    private static Guid UserRefFrom(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return json.RootElement.GetProperty("uref").GetGuid();
    }
}
