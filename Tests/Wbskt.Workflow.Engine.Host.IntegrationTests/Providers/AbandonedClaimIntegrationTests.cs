using FluentAssertions;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// IdempotencyKey_ReleaseAbandonedClaims (#770): a new leader marks the event claims a stopped engine left
/// Pending as Failed, so the broker's redelivery reclaims them instead of dropping them as duplicates, and
/// settles the trigger starts whose run exists, since its recovery runs them.
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
        Guid abandonedToken = Guid.NewGuid();
        await keys.UpsertPendingAsync(inbound, 0, abandonedToken, Guid.Empty, 0, CancellationToken.None);
        foreach (string key in new[] { triggerStart, action, settled })
        {
            await keys.UpsertPendingAsync(key, 0, Guid.NewGuid(), Guid.Empty, 0, CancellationToken.None);
        }

        await keys.MarkSucceededAsync(settled, "{}", CancellationToken.None);

        // The sweep takes every Pending event claim in the shared test database, not only these.
        int released = await keys.ReleaseAbandonedClaimsAsync(CancellationToken.None);

        released.Should().BeGreaterOrEqualTo(2);
        (await ReadKeyAsync(inbound)).Status.Should().Be("Failed");
        (await ReadKeyAsync(triggerStart)).Status.Should().Be("Failed");
        (await ReadKeyAsync(action)).Status.Should().Be("Pending");
        (await ReadKeyAsync(settled)).Status.Should().Be("Succeeded");

        // An overlapping old leader still finishing the claim no longer holds it, so its settle misses.
        IdempotencyKeyRow late = await keys.MarkClaimSucceededAsync(inbound, abandonedToken, "{}", CancellationToken.None);
        late.Status.Should().Be("Failed");
        late.BranchRefId.Should().NotBe(abandonedToken);

        // The redelivery's own claim then takes the released one over.
        Guid redelivery = Guid.NewGuid();
        IdempotencyKeyRow reclaimed = await keys.ReclaimFailedAsync(inbound, redelivery, CancellationToken.None);
        reclaimed.BranchRefId.Should().Be(redelivery);
        reclaimed.Status.Should().Be("Pending");
    }

    [SkippableFact]
    public async Task An_event_claim_left_pending_for_minutes_is_reclaimed_while_a_young_one_and_action_keys_are_not()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var keys = ProviderFactory.IdempotencyKey(fixture.ConnectionString);
        string suffix = Guid.NewGuid().ToString("N");
        string young = $"inbound-event:young-{suffix}";
        string stale = $"inbound-event:{suffix}";
        string staleStart = $"trigger-start:1:{suffix}";
        string staleAction = $"action:1:{suffix}";
        foreach (string key in new[] { young, stale, staleStart, staleAction })
        {
            await keys.UpsertPendingAsync(key, 0, Guid.NewGuid(), Guid.Empty, 0, CancellationToken.None);
        }

        // A holder whose dispatch failed and could not mark its claim Failed either, five minutes ago.
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var backdate = new SqlCommand(
                "UPDATE dbo.IdempotencyKeys SET CreatedAt = DATEADD(MINUTE, -5, SYSUTCDATETIME()) WHERE KeyValue IN (@Stale, @StaleStart, @StaleAction)",
                connection);
            backdate.Parameters.AddWithValue("@Stale", stale);
            backdate.Parameters.AddWithValue("@StaleStart", staleStart);
            backdate.Parameters.AddWithValue("@StaleAction", staleAction);
            await backdate.ExecuteNonQueryAsync();
        }

        Guid redelivery = Guid.NewGuid();
        (await keys.ReclaimFailedAsync(young, redelivery, CancellationToken.None)).BranchRefId.Should().NotBe(redelivery, "a claim taken moments ago may still be going");
        (await keys.ReclaimFailedAsync(staleAction, redelivery, CancellationToken.None)).BranchRefId.Should().NotBe(redelivery, "a Pending action key means run the node again");
        (await keys.ReclaimFailedAsync(staleStart, redelivery, CancellationToken.None)).BranchRefId.Should().Be(redelivery);

        IdempotencyKeyRow reclaimed = await keys.ReclaimFailedAsync(stale, redelivery, CancellationToken.None);
        reclaimed.BranchRefId.Should().Be(redelivery);
        reclaimed.Status.Should().Be("Pending");
        reclaimed.CreatedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1), "the reclaim takes the claim afresh");

        Guid another = Guid.NewGuid();
        (await keys.ReclaimFailedAsync(stale, another, CancellationToken.None)).BranchRefId.Should().Be(redelivery, "the new holder is not reclaimed from at once");
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_trigger_start_whose_run_exists_is_settled_rather_than_released(bool stoppedEngineMarkedItFailed)
    {
        // #833 review: released, the redelivery would reclaim it, find the run and hand its root branch,
        // which recovery already runs, to the pump a second time.
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var keys = ProviderFactory.IdempotencyKey(fixture.ConnectionString);
        string startKey = $"trigger-start:1:{Guid.NewGuid():N}";
        await keys.UpsertPendingAsync(startKey, 0, Guid.NewGuid(), Guid.Empty, 0, CancellationToken.None);
        RunRow run = await CreateRunAsync();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("UPDATE dbo.Runs SET TriggerStartKey = @Key WHERE Id = @Id", connection);
            command.Parameters.AddWithValue("@Key", startKey);
            command.Parameters.AddWithValue("@Id", run.Id);
            await command.ExecuteNonQueryAsync();
        }

        if (stoppedEngineMarkedItFailed)
        {
            await keys.MarkFailedAsync(startKey, "{}", CancellationToken.None);
        }

        await keys.ReleaseAbandonedClaimsAsync(CancellationToken.None);

        (await ReadKeyAsync(startKey)).Status.Should().Be("Succeeded");
    }

    [SkippableFact]
    public async Task Only_the_holder_of_a_claim_settles_it_and_the_resumed_mark_survives_a_reclaim()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var keys = ProviderFactory.IdempotencyKey(fixture.ConnectionString);
        string key = $"inbound-event:{Guid.NewGuid():N}";
        Guid first = Guid.NewGuid();
        await keys.UpsertPendingAsync(key, 0, first, Guid.Empty, 0, CancellationToken.None);
        await keys.MarkResumedAsync(key, first, CancellationToken.None);
        await keys.MarkClaimFailedAsync(key, first, "{}", CancellationToken.None);
        Guid redelivery = Guid.NewGuid();
        await keys.ReclaimFailedAsync(key, redelivery, CancellationToken.None);

        // A late write from the attempt that lost the claim changes nothing.
        await keys.MarkClaimSucceededAsync(key, first, "{}", CancellationToken.None);
        await keys.MarkClaimFailedAsync(key, first, "{}", CancellationToken.None);
        var row = await ReadKeyAsync(key);
        row.Status.Should().Be("Pending");
        row.BranchRefId.Should().Be(redelivery);
        row.ResultJson.Should().Be(IdempotencyClaimResults.Resumed);

        await keys.MarkClaimSucceededAsync(key, redelivery, "{}", CancellationToken.None);
        (await ReadKeyAsync(key)).Status.Should().Be("Succeeded");
    }

    private async Task<(string Status, Guid BranchRefId, string? ResultJson)> ReadKeyAsync(string keyValue)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var select = new SqlCommand(
            "SELECT Status, BranchRefId, ResultJson FROM dbo.IdempotencyKeys WHERE KeyValue = @KeyValue;",
            connection);
        select.Parameters.AddWithValue("@KeyValue", keyValue);
        await using SqlDataReader reader = await select.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue($"the key {keyValue} should have a row");
        return (reader.GetString(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<RunRow> CreateRunAsync()
    {
        WorkflowDefinitionRow wd = await ProviderFactory.WorkflowDefinition(fixture.ConnectionString).InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 1, WorkspaceId = 1,
            Name = $"IT-WD-CLAIM-{Guid.NewGuid():N}", Description = null,
            DefinitionJson = """{"nodes":[],"edges":[]}""", PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None).LiveAsync(fixture.ConnectionString);

        return (await ActiveRunSeeder.StartAsync(fixture.ConnectionString, new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = wd.Id, WorkflowRefId = wd.RefId, WorkflowVersion = wd.Version,
            TriggerNodeId = Guid.NewGuid(), CorrelationKey = null, Status = "Running",
            StartedAt = DateTime.UtcNow, CompletedAt = null, CancellationRequestedAt = null,
            CancellationReason = null, CreditBudget = 0m, CreatedAt = DateTime.UtcNow
        })).Run;
    }
}
