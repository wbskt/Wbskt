using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Task 13.7 — PendingTriggerEvent: verifies FIFO dequeue order and that concurrent
/// dequeues via DELETE OUTPUT return distinct rows (no double-dequeue).
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
            Description = null, IsEnabled = true,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        return (wd.RefId, wd.Id);
    }

    [SkippableFact]
    public async Task DequeueOldest_returns_null_when_queue_empty()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, _) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        var dequeued = await provider.DequeueNextAsync(
            workflowRefId, Guid.NewGuid(), "empty-correlation", CancellationToken.None);

        dequeued.Should().BeNull();
    }

    [SkippableFact]
    public async Task DequeueOldest_returns_oldest_first()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, _) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        string correlationKey = $"order-test-{Guid.NewGuid():N}";
        Guid triggerNodeId = Guid.NewGuid();

        // Enqueue three events with deliberate timing gaps
        PendingTriggerEventRow first = await provider.EnqueueAsync(
            workflowRefId, triggerNodeId, correlationKey, """{"n":1}""", CancellationToken.None);

        await Task.Delay(50); // ensure distinct EnqueuedAt timestamps

        PendingTriggerEventRow second = await provider.EnqueueAsync(
            workflowRefId, triggerNodeId, correlationKey, """{"n":2}""", CancellationToken.None);

        await Task.Delay(50);

        PendingTriggerEventRow third = await provider.EnqueueAsync(
            workflowRefId, triggerNodeId, correlationKey, """{"n":3}""", CancellationToken.None);

        // Dequeue three times; should come out in enqueue order
        var d1 = await provider.DequeueNextAsync(workflowRefId, triggerNodeId, correlationKey, CancellationToken.None);
        var d2 = await provider.DequeueNextAsync(workflowRefId, triggerNodeId, correlationKey, CancellationToken.None);
        var d3 = await provider.DequeueNextAsync(workflowRefId, triggerNodeId, correlationKey, CancellationToken.None);
        var d4 = await provider.DequeueNextAsync(workflowRefId, triggerNodeId, correlationKey, CancellationToken.None);

        d1.Should().NotBeNull();
        d2.Should().NotBeNull();
        d3.Should().NotBeNull();
        d4.Should().BeNull("queue should be empty after three dequeues");

        d1!.EnqueuedAt.Should().BeBefore(d2!.EnqueuedAt);
        d2.EnqueuedAt.Should().BeBefore(d3!.EnqueuedAt);
    }

    [SkippableFact]
    public async Task Dequeue_does_not_cross_trigger_nodes()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, _) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        // Two triggers in the SAME workflow sharing a correlation key. Draining one must never
        // consume the other's queued event - doing so would start a run from the wrong trigger
        // node with the wrong payload.
        string correlationKey = $"shared-key-{Guid.NewGuid():N}";
        Guid triggerA = Guid.NewGuid();
        Guid triggerB = Guid.NewGuid();

        await provider.EnqueueAsync(workflowRefId, triggerA, correlationKey, """{"trigger":"A"}""", CancellationToken.None);
        await provider.EnqueueAsync(workflowRefId, triggerB, correlationKey, """{"trigger":"B"}""", CancellationToken.None);

        var fromA = await provider.DequeueNextAsync(workflowRefId, triggerA, correlationKey, CancellationToken.None);
        fromA.Should().NotBeNull();
        fromA!.TriggerNodeId.Should().Be(triggerA);
        fromA.InboundEventJson.Should().Contain("A");

        // A's queue is now empty; B's event must still be there, untouched.
        var aAgain = await provider.DequeueNextAsync(workflowRefId, triggerA, correlationKey, CancellationToken.None);
        aAgain.Should().BeNull("draining trigger A must not reach across to trigger B's event");

        var fromB = await provider.DequeueNextAsync(workflowRefId, triggerB, correlationKey, CancellationToken.None);
        fromB.Should().NotBeNull();
        fromB!.TriggerNodeId.Should().Be(triggerB);
        fromB.InboundEventJson.Should().Contain("B");
    }

    [SkippableFact]
    public async Task Concurrent_dequeues_return_distinct_rows()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var (workflowRefId, _) = await CreateWorkflowAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        const int rowCount = 20;
        const int callerCount = 10;
        string correlationKey = $"concurrent-{Guid.NewGuid():N}";
        Guid triggerNodeId = Guid.NewGuid();

        for (int i = 0; i < rowCount; i++)
        {
            await provider.EnqueueAsync(
                workflowRefId, triggerNodeId, correlationKey, $"{{\"seq\":{i}}}", CancellationToken.None);
        }

        var dequeued = new System.Collections.Concurrent.ConcurrentBag<long>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, callerCount),
            new ParallelOptions { MaxDegreeOfParallelism = callerCount },
            async (_, ct) =>
            {
                var p = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);
                var row = await p.DequeueNextAsync(workflowRefId, triggerNodeId, correlationKey, ct);
                if (row is not null)
                {
                    dequeued.Add(row.Id);
                }
            });

        // All dequeued Ids must be distinct — no double-dequeue
        dequeued.Should().OnlyHaveUniqueItems("DELETE OUTPUT should be atomic — no row dequeued twice");
        dequeued.Count.Should().BeLessOrEqualTo(rowCount);
    }
}
