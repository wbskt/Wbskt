using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// PendingTriggerEvent: the queue's head is leased in FIFO order, a leased head holds its place until it
/// is deleted or its lease runs out, and concurrent drains never lease the same row twice.
/// </summary>
[Collection("SqlEdge")]
public sealed class PendingTriggerEventQueueIntegrationTests(SqlEdgeFixture fixture)
{
    private async Task<(Guid WorkflowRefId, int WorkflowDefinitionId)> CreateWorkflowAsync()
    {
        var wdProvider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        WorkflowDefinitionRow wd = await wdProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 0, WorkspaceId = 1,
            Name = $"IT-WD-PTE-{Guid.NewGuid():N}",
            Description = null,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None).LiveAsync(fixture.ConnectionString);

        return (wd.RefId, wd.Id);
    }

    [SkippableFact]
    public async Task DequeueOldest_returns_null_when_queue_empty()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, _) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        var dequeued = await provider.LeaseNextAsync(
            workflowRefId, Guid.NewGuid(), "empty-correlation", 60, CancellationToken.None);

        dequeued.Should().BeNull();
    }

    [SkippableFact]
    public async Task DequeueOldest_returns_oldest_first()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, workflowDefinitionId) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        string correlationKey = $"order-test-{Guid.NewGuid():N}";
        Guid triggerNodeId = Guid.NewGuid();
        await ActiveRunSeeder.StartAsync(fixture.ConnectionString, workflowDefinitionId, workflowRefId, triggerNodeId, correlationKey);

        // Enqueue three events with deliberate timing gaps
        PendingTriggerEventRow first = (await provider.EnqueueBehindActiveRunAsync(
            workflowRefId, triggerNodeId, correlationKey, """{"n":1}""", CancellationToken.None))!;

        await Task.Delay(50); // ensure distinct EnqueuedAt timestamps

        PendingTriggerEventRow second = (await provider.EnqueueBehindActiveRunAsync(
            workflowRefId, triggerNodeId, correlationKey, """{"n":2}""", CancellationToken.None))!;

        await Task.Delay(50);

        PendingTriggerEventRow third = (await provider.EnqueueBehindActiveRunAsync(
            workflowRefId, triggerNodeId, correlationKey, """{"n":3}""", CancellationToken.None))!;

        // Lease and delete three times; they come out in enqueue order.
        var d1 = await provider.LeaseNextAsync(workflowRefId, triggerNodeId, correlationKey, 60, CancellationToken.None);
        await provider.DeleteByIdAsync(d1!.Id, CancellationToken.None);
        var d2 = await provider.LeaseNextAsync(workflowRefId, triggerNodeId, correlationKey, 60, CancellationToken.None);
        await provider.DeleteByIdAsync(d2!.Id, CancellationToken.None);
        var d3 = await provider.LeaseNextAsync(workflowRefId, triggerNodeId, correlationKey, 60, CancellationToken.None);
        await provider.DeleteByIdAsync(d3!.Id, CancellationToken.None);
        var d4 = await provider.LeaseNextAsync(workflowRefId, triggerNodeId, correlationKey, 60, CancellationToken.None);

        d4.Should().BeNull("queue should be empty after three deletes");
        new[] { d1.Id, d2.Id, d3.Id }.Should().Equal(first.Id, second.Id, third.Id);
    }

    [SkippableFact]
    public async Task A_leased_head_holds_its_place_until_its_lease_runs_out()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, workflowDefinitionId) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);
        string correlationKey = $"lease-test-{Guid.NewGuid():N}";
        Guid triggerNodeId = Guid.NewGuid();
        await ActiveRunSeeder.StartAsync(fixture.ConnectionString, workflowDefinitionId, workflowRefId, triggerNodeId, correlationKey);
        PendingTriggerEventRow first = (await provider.EnqueueBehindActiveRunAsync(workflowRefId, triggerNodeId, correlationKey, """{"n":1}""", CancellationToken.None))!;
        await Task.Delay(50);
        await provider.EnqueueBehindActiveRunAsync(workflowRefId, triggerNodeId, correlationKey, """{"n":2}""", CancellationToken.None);

        // A replay whose start failed leaves its row leased: the event behind it must not jump ahead.
        (await provider.LeaseNextAsync(workflowRefId, triggerNodeId, correlationKey, 1, CancellationToken.None))!.Id.Should().Be(first.Id);
        (await provider.LeaseNextAsync(workflowRefId, triggerNodeId, correlationKey, 60, CancellationToken.None)).Should().BeNull();

        // Once the lease runs out the same event is handed out again rather than lost.
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        (await provider.LeaseNextAsync(workflowRefId, triggerNodeId, correlationKey, 60, CancellationToken.None))!.Id.Should().Be(first.Id);
    }

    [SkippableFact]
    public async Task Idle_keys_are_the_queues_with_no_run_going()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, workflowDefinitionId) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);
        Guid triggerNodeId = Guid.NewGuid();
        string busyKey = $"busy-{Guid.NewGuid():N}";
        string idleKey = $"idle-{Guid.NewGuid():N}";
        await ActiveRunSeeder.StartAsync(fixture.ConnectionString, workflowDefinitionId, workflowRefId, triggerNodeId, busyKey);
        await provider.EnqueueBehindActiveRunAsync(workflowRefId, triggerNodeId, busyKey, "{}", CancellationToken.None);

        // A queue whose run ended before its drain: inserted directly, as Enqueue only queues behind a run.
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new SqlCommand(
                "INSERT INTO dbo.PendingTriggerEvents (WorkflowRefId, TriggerNodeId, CorrelationKey, InboundEventJson) VALUES (@WorkflowRefId, @TriggerNodeId, @CorrelationKey, N'{}');",
                connection);
            insert.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
            insert.Parameters.AddWithValue("@TriggerNodeId", triggerNodeId);
            insert.Parameters.AddWithValue("@CorrelationKey", idleKey);
            await insert.ExecuteNonQueryAsync();
        }

        IReadOnlyCollection<PendingTriggerEventKey> idle = await provider.GetIdleKeysAsync(1000, CancellationToken.None);

        idle.Should().Contain(new PendingTriggerEventKey(workflowRefId, triggerNodeId, idleKey));
        idle.Should().NotContain(key => key.CorrelationKey == busyKey);
    }

    [SkippableFact]
    public async Task Dequeue_does_not_cross_trigger_nodes()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, workflowDefinitionId) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        // Two triggers in the SAME workflow sharing a correlation key. Draining one must never
        // consume the other's queued event - doing so would start a run from the wrong trigger
        // node with the wrong payload.
        string correlationKey = $"shared-key-{Guid.NewGuid():N}";
        Guid triggerA = Guid.NewGuid();
        Guid triggerB = Guid.NewGuid();
        await ActiveRunSeeder.StartAsync(fixture.ConnectionString, workflowDefinitionId, workflowRefId, triggerA, correlationKey);
        await ActiveRunSeeder.StartAsync(fixture.ConnectionString, workflowDefinitionId, workflowRefId, triggerB, correlationKey);

        await provider.EnqueueBehindActiveRunAsync(workflowRefId, triggerA, correlationKey, """{"trigger":"A"}""", CancellationToken.None);
        await provider.EnqueueBehindActiveRunAsync(workflowRefId, triggerB, correlationKey, """{"trigger":"B"}""", CancellationToken.None);

        var fromA = await provider.LeaseNextAsync(workflowRefId, triggerA, correlationKey, 60, CancellationToken.None);
        fromA.Should().NotBeNull();
        fromA!.TriggerNodeId.Should().Be(triggerA);
        fromA.InboundEventJson.Should().Contain("A");
        await provider.DeleteByIdAsync(fromA.Id, CancellationToken.None);

        // A's queue is now empty; B's event must still be there, untouched.
        var aAgain = await provider.LeaseNextAsync(workflowRefId, triggerA, correlationKey, 60, CancellationToken.None);
        aAgain.Should().BeNull("draining trigger A must not reach across to trigger B's event");

        var fromB = await provider.LeaseNextAsync(workflowRefId, triggerB, correlationKey, 60, CancellationToken.None);
        fromB.Should().NotBeNull();
        fromB!.TriggerNodeId.Should().Be(triggerB);
        fromB.InboundEventJson.Should().Contain("B");
    }

    [SkippableFact]
    public async Task Concurrent_drains_lease_the_head_once()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, workflowDefinitionId) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        const int rowCount = 20;
        const int callerCount = 10;
        string correlationKey = $"concurrent-{Guid.NewGuid():N}";
        Guid triggerNodeId = Guid.NewGuid();
        await ActiveRunSeeder.StartAsync(fixture.ConnectionString, workflowDefinitionId, workflowRefId, triggerNodeId, correlationKey);

        for (int i = 0; i < rowCount; i++)
        {
            await provider.EnqueueBehindActiveRunAsync(
                workflowRefId, triggerNodeId, correlationKey, $"{{\"seq\":{i}}}", CancellationToken.None);
        }

        var dequeued = new System.Collections.Concurrent.ConcurrentBag<long>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, callerCount),
            new ParallelOptions { MaxDegreeOfParallelism = callerCount },
            async (_, ct) =>
            {
                var p = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);
                var row = await p.LeaseNextAsync(workflowRefId, triggerNodeId, correlationKey, 60, ct);
                if (row is not null)
                {
                    dequeued.Add(row.Id);
                }
            });

        // Only the head is ever handed out, and only to one caller while its lease holds.
        dequeued.Should().ContainSingle("the head is leased by one drain and the rest wait behind it");
    }
}
