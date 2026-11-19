using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Host.Tests.HostedServices;

public sealed class PendingTriggerEventBacklogReaperTests
{
    [Fact]
    public async Task Tick_deletes_expired_events()
    {
        // Arrange
        var leadership = new FixedLeadershipGate(true);
        var provider = new RecordingPendingTriggerEventProvider();
        var drainer = new RecordingDrainer();
        var reaper = new PendingTriggerEventBacklogReaper(new FixedClock(), leadership, WorkerTestServices.Scopes(provider, drainer), NullLogger<PendingTriggerEventBacklogReaper>.Instance, WorkerTestServices.Options());

        // Act
        await reaper.TickAsync(CancellationToken.None);

        // Assert
        Assert.Equal((new DateTime(2026, 5, 25, 12, 30, 0, DateTimeKind.Utc), 1000), provider.LastDeleteRequest);
    }

    [Fact]
    public async Task Tick_skips_when_lease_not_held()
    {
        // Arrange
        var leadership = new FixedLeadershipGate(false);
        var provider = new RecordingPendingTriggerEventProvider();
        var drainer = new RecordingDrainer();
        var reaper = new PendingTriggerEventBacklogReaper(new FixedClock(), leadership, WorkerTestServices.Scopes(provider, drainer), NullLogger<PendingTriggerEventBacklogReaper>.Instance, WorkerTestServices.Options());

        // Act
        await reaper.TickAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, leadership.Checks);
        Assert.Null(provider.LastDeleteRequest);
    }

    [Fact]
    public async Task Tick_drains_each_queue_with_no_run_going_and_carries_on_past_a_failure()
    {
        // Arrange
        var provider = new RecordingPendingTriggerEventProvider();
        var first = new PendingTriggerEventKey(Guid.NewGuid(), Guid.NewGuid(), "corr-1");
        var second = new PendingTriggerEventKey(Guid.NewGuid(), Guid.NewGuid(), "corr-2");
        provider.IdleKeys.AddRange([first, second]);
        var drainer = new RecordingDrainer { FailFor = first };
        var reaper = new PendingTriggerEventBacklogReaper(new FixedClock(), new FixedLeadershipGate(true), WorkerTestServices.Scopes(provider, drainer), NullLogger<PendingTriggerEventBacklogReaper>.Instance, WorkerTestServices.Options());

        // Act
        await reaper.TickAsync(CancellationToken.None);

        // Assert
        Assert.Equal([first, second], drainer.Drained);
    }

    private sealed class RecordingDrainer : IPendingTriggerEventDrainer
    {
        public PendingTriggerEventKey? FailFor { get; init; }
        public List<PendingTriggerEventKey> Drained { get; } = [];

        public Task DrainAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
        {
            var key = new PendingTriggerEventKey(workflowRefId, triggerNodeId, correlationKey);
            Drained.Add(key);
            return key == FailFor ? throw new InvalidOperationException("start failed") : Task.CompletedTask;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;

        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class FixedLeadershipGate(bool isActive) : ILeadershipGate
    {
        public int Checks { get; private set; }

        public bool IsActive
        {
            get
            {
                Checks++;
                return isActive;
            }
        }
    }

    private sealed class RecordingPendingTriggerEventProvider : IPendingTriggerEventProvider
    {
        public (DateTime CutoffUtc, int BatchSize)? LastDeleteRequest { get; private set; }

        public Task<PendingTriggerEventRow?> EnqueueBehindAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, long? exceptPendingEventId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PendingTriggerEventRow?> LeaseNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, int leaseSeconds, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public List<PendingTriggerEventKey> IdleKeys { get; } = [];
        public Task<IReadOnlyCollection<PendingTriggerEventKey>> GetIdleKeysAsync(int batchSize, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<PendingTriggerEventKey>>(IdleKeys);

        public Task<long> CountAllAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            LastDeleteRequest = (cutoffUtc, batchSize);
            return Task.FromResult(1);
        }
    }
}

