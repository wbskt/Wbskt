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
    public async Task Tick_skips_when_lease_not_held()
    {
        var leadership = new FixedLeadershipGate(false);
        var runProvider = new RecordingRunRetentionProvider([HistoryRetentionGc.RunBatchSize]);
        var gc = new HistoryRetentionGc(new FixedClock(), leadership, WorkerTestServices.Scopes(runProvider), NullLogger<HistoryRetentionGc>.Instance, WorkerTestServices.Options(), Catalog());

        await gc.TickAsync(CancellationToken.None);

        Assert.Equal(1, leadership.Checks);
        Assert.Empty(runProvider.DeleteCalls);
    }

    [Fact]
    public async Task Tick_deletes_runs_past_the_elevated_window_until_caught_up()
    {
        // #774: runs go with their history by plan; no pass prunes a kept run's history at a global window.
        var runProvider = new RecordingRunRetentionProvider([HistoryRetentionGc.RunBatchSize, 3]);
        var gc = new HistoryRetentionGc(new FixedClock(), new FixedLeadershipGate(true), WorkerTestServices.Scopes(runProvider), NullLogger<HistoryRetentionGc>.Instance, WorkerTestServices.Options(new WorkflowEngineOptions { HistoryRetentionWindowElevated = TimeSpan.FromDays(365) }), Catalog());

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
        var gc = new HistoryRetentionGc(new FixedClock(), new FixedLeadershipGate(true), WorkerTestServices.Scopes(runProvider), NullLogger<HistoryRetentionGc>.Instance, WorkerTestServices.Options(), catalog);

        await gc.TickAsync(CancellationToken.None);

        var retention = Assert.Single(runProvider.DeleteCalls).Retention;
        Assert.Equal(
            [new RunHistoryRetention(PlanCode.Free, 7), new RunHistoryRetention(PlanCode.Maker, 21), new RunHistoryRetention(PlanCode.Pro, 30), new RunHistoryRetention(PlanCode.Team, 30), new RunHistoryRetention(PlanCode.Business, 90)],
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
}
