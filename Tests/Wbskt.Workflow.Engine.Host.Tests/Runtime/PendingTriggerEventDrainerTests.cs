using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

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
                EnqueuedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
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
        Assert.Equal(1, provider.DequeueCalls);
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

        public Task<PendingTriggerEventRow> EnqueueAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<PendingTriggerEventRow?> DequeueNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
        {
            DequeueCalls++;
            return Task.FromResult(rows.Count > 0 ? rows.Dequeue() : null);
        }
        public Task DeleteAllByRunKeyAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountAllAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<PendingTriggerEventRow?> DequeueNextAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingInboundHub : IInboundHub
    {
        public List<InboundEvent> Events { get; } = [];

        public Task<TriggerDispatchResult> HandleAsync(InboundEvent evt, CancellationToken ct)
        {
            Events.Add(evt);
            return Task.FromResult(new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "none"));
        }
    }
}

