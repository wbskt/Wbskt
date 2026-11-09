using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Host.Tests.HostedServices;

public sealed class HistoryRetentionGcTests
{
    [Fact]
    public async Task Tick_loops_until_caught_up()
    {
        // Arrange
        var leadership = new FixedLeadershipGate(true);
        var historyProvider = new RecordingHistoryEventProvider([5000, 100]);
        var gc = new HistoryRetentionGc(new FixedClock(), leadership, WorkerTestServices.Scopes(historyProvider), NullLogger<HistoryRetentionGc>.Instance, WorkerTestServices.Options(), Catalog());

        // Act
        await gc.TickAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, historyProvider.DeleteCalls.Count);
        Assert.All(historyProvider.DeleteCalls, call => Assert.Equal((new DateTime(2026, 4, 26, 12, 30, 0, DateTimeKind.Utc), 5000), call));
    }

    [Fact]
    public async Task Tick_skips_when_lease_not_held()
    {
        // Arrange
        var leadership = new FixedLeadershipGate(false);
        var historyProvider = new RecordingHistoryEventProvider([5000]);
        var gc = new HistoryRetentionGc(new FixedClock(), leadership, WorkerTestServices.Scopes(historyProvider), NullLogger<HistoryRetentionGc>.Instance, WorkerTestServices.Options(), Catalog());

        // Act
        await gc.TickAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, leadership.Checks);
        Assert.Empty(historyProvider.DeleteCalls);
    }

    [Fact]
    public async Task Tick_then_deletes_runs_past_the_elevated_window_until_caught_up()
    {
        var historyProvider = new RecordingHistoryEventProvider([0]);
        var runProvider = new RecordingRunRetentionProvider([HistoryRetentionGc.RunBatchSize, 3]);
        var gc = new HistoryRetentionGc(new FixedClock(), new FixedLeadershipGate(true), WorkerTestServices.Scopes(historyProvider, runProvider), NullLogger<HistoryRetentionGc>.Instance, WorkerTestServices.Options(new WorkflowEngineOptions { HistoryRetentionWindowElevated = TimeSpan.FromDays(365) }), Catalog());

        await gc.TickAsync(CancellationToken.None);

        var now = new DateTime(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
        var expected = (now, now.AddDays(-365), HistoryRetentionGc.RunBatchSize);
        Assert.Equal([expected, expected], runProvider.DeleteCalls.Select(c => (c.NowUtc, c.CutoffUtc, c.BatchSize)));
    }

    [Fact]
    public async Task Tick_passes_each_plans_run_history_days_with_configuration_applied()
    {
        var runProvider = new RecordingRunRetentionProvider([0]);
        var catalog = new PlanCatalog(Microsoft.Extensions.Options.Options.Create(new PlanCatalogOptions
        {
            Plans = new() { [PlanCode.Maker] = new PlanLimitOverrides { RunHistoryDays = 21 } }
        }));
        var gc = new HistoryRetentionGc(new FixedClock(), new FixedLeadershipGate(true), WorkerTestServices.Scopes(new RecordingHistoryEventProvider([0]), runProvider), NullLogger<HistoryRetentionGc>.Instance, WorkerTestServices.Options(), catalog);

        await gc.TickAsync(CancellationToken.None);

        var retention = Assert.Single(runProvider.DeleteCalls).Retention;
        Assert.Equal(
            [new RunHistoryRetention(PlanCode.Free, 7), new RunHistoryRetention(PlanCode.Maker, 21), new RunHistoryRetention(PlanCode.Pro, 30), new RunHistoryRetention(PlanCode.Business, 30)],
            retention.OrderBy(r => r.PlanCode));
    }

    private static PlanCatalog Catalog() => new(Microsoft.Extensions.Options.Options.Create(new PlanCatalogOptions()));

    private sealed class RecordingRunRetentionProvider(IReadOnlyCollection<int> deletes) : IRunRetentionProvider
    {
        private readonly Queue<int> _deletes = new(deletes);

        public List<(DateTime NowUtc, IReadOnlyCollection<RunHistoryRetention> Retention, DateTime CutoffUtc, int BatchSize)> DeleteCalls { get; } = [];

        public Task<int> DeleteRetiredRunsAsync(DateTime nowUtc, IReadOnlyCollection<RunHistoryRetention> retention, DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            DeleteCalls.Add((nowUtc, retention, cutoffUtc, batchSize));
            return Task.FromResult(_deletes.Dequeue());
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
