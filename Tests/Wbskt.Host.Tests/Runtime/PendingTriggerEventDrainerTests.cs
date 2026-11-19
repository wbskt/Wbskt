using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.Runtime;

public sealed class PendingTriggerEventDrainerTests
{
    [Fact]
    public async Task Drain_dequeues_one_and_replays_via_InboundHub()
    {
        // Arrange
        var provider = new RecordingPendingTriggerEventProvider(new Queue<PendingTriggerEventRow>([
            new PendingTriggerEventRow
            {
                Id = 1,
                WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                TriggerNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationKey = "client:serial-1:telemetry",
                InboundEventJson = JsonSerializer.Serialize(new InboundEvent(
                    "client",
                    ["client:serial-1:telemetry"],
                    "evt-1",
                    new Dictionary<string, JsonElement>
                    {
                        ["clientRefId"] = JsonSerializer.SerializeToElement("serial-1"),
                        ["messageType"] = JsonSerializer.SerializeToElement("telemetry")
                    },
                    DateTime.UtcNow)),
                EnqueuedAt = DateTime.UtcNow
            }
        ]));
        var inboundHub = new RecordingInboundHub();
        var drainer = new PendingTriggerEventDrainer(provider, inboundHub);

        // Act
        await drainer.DrainAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "client:serial-1:telemetry", CancellationToken.None);

        // Assert: exactly one dequeue - draining is one-event-per-run-terminal, not a loop - and the
        // replayed event carries a re-minted id so BookmarkResumer's idempotency claim doesn't treat
        // it as a duplicate of the original (already-Succeeded) delivery.
        Assert.Single(inboundHub.Events);
        Assert.Equal("evt-1:drain:1", inboundHub.Events.Single().InboundEventId);
        // ...and goes only to the trigger that queued it (#316).
        Assert.Equal(
            new QueuedTrigger(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            inboundHub.Events.Single().ReplayFor);
        Assert.Equal(1, provider.DequeueCalls);
        // ...and the row goes only once the replay was handed over.
        Assert.Equal([1L], provider.Deleted);
    }

    [Fact]
    public async Task A_replay_whose_start_fails_keeps_its_row_for_a_retry()
    {
        // Arrange
        var provider = new RecordingPendingTriggerEventProvider(new Queue<PendingTriggerEventRow>([QueuedRow(3, """{"channelKind":"manual","matchKeys":["manual:x"],"inboundEventId":"evt-3","payload":{},"receivedAt":"2026-10-13T12:00:00Z"}""")]));
        var drainer = new PendingTriggerEventDrainer(provider, new RecordingInboundHub { Throws = true });

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => drainer.DrainAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "manual:x", CancellationToken.None));

        // Assert: still leased, so it is replayed again once the lease runs out.
        Assert.Empty(provider.Deleted);
    }

    [Fact]
    public async Task An_unreadable_event_is_dropped_so_it_does_not_hold_the_queue()
    {
        // Arrange
        var provider = new RecordingPendingTriggerEventProvider(new Queue<PendingTriggerEventRow>([QueuedRow(4, "not json")]));
        var inboundHub = new RecordingInboundHub();
        var drainer = new PendingTriggerEventDrainer(provider, inboundHub);

        // Act
        await drainer.DrainAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "manual:x", CancellationToken.None);

        // Assert
        Assert.Empty(inboundHub.Events);
        Assert.Equal([4L], provider.Deleted);
    }

    private static PendingTriggerEventRow QueuedRow(long id, string json) => new()
    {
        Id = id,
        WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        TriggerNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        CorrelationKey = "manual:x",
        InboundEventJson = json,
        EnqueuedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task A_stored_event_cannot_claim_to_start_a_child_run()
    {
        // StartsChildRun is never read from JSON, so neither a queued event nor anything else that
        // deserializes one can carry it. The replay is exempt from the run ceiling by ReplayFor instead.
        var provider = new RecordingPendingTriggerEventProvider(new Queue<PendingTriggerEventRow>([
            new PendingTriggerEventRow
            {
                Id = 2,
                WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                TriggerNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationKey = "manual:x",
                InboundEventJson = """
                    {"channelKind":"manual","matchKeys":["manual:x"],"inboundEventId":"evt-2","payload":{},"receivedAt":"2026-10-13T12:00:00Z","startsChildRun":true}
                    """,
                EnqueuedAt = DateTime.UtcNow
            }
        ]));
        var inboundHub = new RecordingInboundHub();
        var drainer = new PendingTriggerEventDrainer(provider, inboundHub);

        await drainer.DrainAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "manual:x", CancellationToken.None);

        InboundEvent replayed = Assert.Single(inboundHub.Events);
        Assert.False(replayed.StartsChildRun);
        Assert.NotNull(replayed.ReplayFor);
        Assert.DoesNotContain("startsChildRun", JsonSerializer.Serialize(replayed with { StartsChildRun = true }), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Drain_does_nothing_when_queue_is_empty()
    {
        // Arrange
        var provider = new RecordingPendingTriggerEventProvider(new Queue<PendingTriggerEventRow>());
        var inboundHub = new RecordingInboundHub();
        var drainer = new PendingTriggerEventDrainer(provider, inboundHub);

        // Act
        await drainer.DrainAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "client:serial-1:telemetry", CancellationToken.None);

        // Assert
        Assert.Empty(inboundHub.Events);
        Assert.Equal(1, provider.DequeueCalls);
    }

    private sealed class RecordingPendingTriggerEventProvider(Queue<PendingTriggerEventRow> rows) : IPendingTriggerEventProvider
    {
        public int DequeueCalls { get; private set; }
        public List<long> Deleted { get; } = [];

        public Task<PendingTriggerEventRow?> EnqueueBehindActiveRunAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<PendingTriggerEventRow?> LeaseNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, int leaseSeconds, CancellationToken ct)
        {
            DequeueCalls++;
            return Task.FromResult(rows.Count > 0 ? rows.Dequeue() : null);
        }
        public Task DeleteByIdAsync(long id, CancellationToken ct)
        {
            Deleted.Add(id);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<PendingTriggerEventKey>> GetIdleKeysAsync(int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountAllAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

    }

    private sealed class RecordingInboundHub : IInboundHub
    {
        public List<InboundEvent> Events { get; } = [];
        public bool Throws { get; init; }

        public Task<TriggerDispatchResult> HandleAsync(InboundEvent evt, CancellationToken ct)
        {
            Events.Add(evt);
            if (Throws)
            {
                throw new InvalidOperationException("start failed");
            }

            return Task.FromResult(new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "none"));
        }
    }
}

