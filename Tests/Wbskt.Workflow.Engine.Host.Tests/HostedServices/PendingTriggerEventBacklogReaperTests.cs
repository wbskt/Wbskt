using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class PendingTriggerEventBacklogReaperTests
{
    [Fact]
    public async Task Tick_deletes_expired_events()
    {
        // Arrange
        var leaseHolder = new RecordingLeaseHolder(isHeld: true);
        var provider = new RecordingPendingTriggerEventProvider();
        var reaper = new PendingTriggerEventBacklogReaper(new FixedClock(), leaseHolder, provider, NullLogger<PendingTriggerEventBacklogReaper>.Instance);

        // Act
        await reaper.ProcessBacklogAsync(CancellationToken.None);

        // Assert
        Assert.Equal((new DateTime(2026, 5, 25, 12, 30, 0, DateTimeKind.Utc), 1000), provider.LastDeleteRequest);
    }

    [Fact]
    public async Task Tick_skips_when_lease_not_held()
    {
        // Arrange
        var leaseHolder = new RecordingLeaseHolder(isHeld: false);
        var provider = new RecordingPendingTriggerEventProvider();
        var reaper = new PendingTriggerEventBacklogReaper(new FixedClock(), leaseHolder, provider, NullLogger<PendingTriggerEventBacklogReaper>.Instance);

        // Act
        await reaper.ProcessBacklogAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["pending-trigger-backlog-reaper"], leaseHolder.IsHeldCalls);
        Assert.Null(provider.LastDeleteRequest);
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class RecordingLeaseHolder(bool isHeld) : ILeaseHolder
    {
        public List<string> IsHeldCalls { get; } = [];

        public Task<bool> TryAcquireAsync(string leaseName, CancellationToken ct) => Task.FromResult(isHeld);
        public Task ReleaseAsync(string leaseName, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> IsHeldAsync(string leaseName, CancellationToken ct)
        {
            IsHeldCalls.Add(leaseName);
            return Task.FromResult(isHeld);
        }
    }

    private sealed class RecordingPendingTriggerEventProvider : IPendingTriggerEventProvider
    {
        public (DateTime CutoffUtc, int BatchSize)? LastDeleteRequest { get; private set; }

        public Task<Wbskt.Workflow.Abstraction.Entities.PendingTriggerEventRow> EnqueueAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<Wbskt.Workflow.Abstraction.Entities.PendingTriggerEventRow?> DequeueNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunKeyAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountAllAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            LastDeleteRequest = (cutoffUtc, batchSize);
            return Task.FromResult(1);
        }
    }
}

