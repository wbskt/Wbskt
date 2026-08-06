using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class HistoryRetentionGcTests
{
    [Fact]
    public async Task Tick_loops_until_caught_up()
    {
        // Arrange
        var leaseHolder = new RecordingLeaseHolder(isHeld: true);
        var historyProvider = new RecordingHistoryEventProvider([5000, 100]);
        var gc = new HistoryRetentionGc(new FixedClock(), leaseHolder, historyProvider, NullLogger<HistoryRetentionGc>.Instance);

        // Act
        await gc.ProcessRetentionAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, historyProvider.DeleteCalls.Count);
        Assert.All(historyProvider.DeleteCalls, call => Assert.Equal((new DateTime(2026, 4, 26, 12, 30, 0, DateTimeKind.Utc), 5000), call));
    }

    [Fact]
    public async Task Tick_skips_when_lease_not_held()
    {
        // Arrange
        var leaseHolder = new RecordingLeaseHolder(isHeld: false);
        var historyProvider = new RecordingHistoryEventProvider([5000]);
        var gc = new HistoryRetentionGc(new FixedClock(), leaseHolder, historyProvider, NullLogger<HistoryRetentionGc>.Instance);

        // Act
        await gc.ProcessRetentionAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["history-retention-gc"], leaseHolder.IsHeldCalls);
        Assert.Empty(historyProvider.DeleteCalls);
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

    private sealed class RecordingHistoryEventProvider : IHistoryEventProvider
    {
        private readonly Queue<int> _deletes;

        public RecordingHistoryEventProvider(IReadOnlyCollection<int> deletes)
        {
            _deletes = new Queue<int>(deletes);
        }

        public List<(DateTime CutoffUtc, int BatchSize)> DeleteCalls { get; } = [];

        public Task InsertBatchAsync(IReadOnlyCollection<Wbskt.Workflow.Abstraction.Entities.HistoryEventRow> events, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<Wbskt.Workflow.Abstraction.Entities.HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, DateTime? elevatedCutoffUtc, CancellationToken ct)
        {
            DeleteCalls.Add((cutoffUtc, batchSize));
            return Task.FromResult(_deletes.Dequeue());
        }
    }
}
