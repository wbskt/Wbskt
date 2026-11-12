using FluentAssertions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// IdempotencyKey_FailAbandonedClaims (#770): a new leader marks the event claims a stopped engine left
/// Pending as Failed, so the broker's redelivery reclaims them instead of dropping them as duplicates.
/// </summary>
[Collection("SqlEdge")]
public sealed class AbandonedClaimIntegrationTests(SqlEdgeFixture fixture)
{
    [SkippableFact]
    public async Task Pending_event_claims_are_released_and_then_reclaimed_while_other_keys_are_left_alone()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var keys = ProviderFactory.IdempotencyKey(fixture.ConnectionString);
        string suffix = Guid.NewGuid().ToString("N");
        string inbound = $"inbound-event:{suffix}";
        string triggerStart = $"trigger-start:1:{suffix}";
        string action = $"action:1:{suffix}";
        string settled = $"inbound-event:settled-{suffix}";
        foreach (string key in new[] { inbound, triggerStart, action, settled })
        {
            await keys.UpsertPendingAsync(key, 0, Guid.NewGuid(), Guid.Empty, 0, CancellationToken.None);
        }

        await keys.MarkSucceededAsync(settled, "{}", CancellationToken.None);

        int released = await keys.FailAbandonedClaimsAsync(CancellationToken.None);

        released.Should().BeGreaterOrEqualTo(2);
        (await keys.GetByKeyAsync(inbound, CancellationToken.None)).Status.Should().Be("Failed");
        (await keys.GetByKeyAsync(triggerStart, CancellationToken.None)).Status.Should().Be("Failed");
        (await keys.GetByKeyAsync(action, CancellationToken.None)).Status.Should().Be("Pending");
        (await keys.GetByKeyAsync(settled, CancellationToken.None)).Status.Should().Be("Succeeded");

        // The redelivery's own claim then takes the released one over.
        Guid redelivery = Guid.NewGuid();
        IdempotencyKeyRow reclaimed = await keys.ReclaimFailedAsync(inbound, redelivery, CancellationToken.None);
        reclaimed.BranchRefId.Should().Be(redelivery);
        reclaimed.Status.Should().Be("Pending");
    }
}
