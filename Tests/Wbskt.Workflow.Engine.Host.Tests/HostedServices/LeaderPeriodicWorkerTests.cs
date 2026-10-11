using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class LeaderPeriodicWorkerTests
{
    [Fact]
    public async Task A_standby_does_no_work()
    {
        var worker = new CountingWorker(new FixedLeadershipGate(false), WorkerTestServices.Scopes());

        await worker.TickAsync(CancellationToken.None);

        Assert.Equal(0, worker.Runs);
    }

    [Fact]
    public async Task Each_tick_gets_its_own_scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedMarker>();
        var worker = new CountingWorker(new FixedLeadershipGate(true), services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        await worker.TickAsync(CancellationToken.None);
        await worker.TickAsync(CancellationToken.None);

        Assert.Equal(2, worker.Markers.Distinct().Count());
    }

    [Fact]
    public async Task A_failing_tick_is_logged_and_the_loop_carries_on()
    {
        var worker = new CountingWorker(new FixedLeadershipGate(true), WorkerTestServices.Scopes(), fail: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(cts.Token);
        await worker.WaitForRunsAsync(3, cts.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(worker.Runs >= 3);
    }

    [Fact]
    public async Task Ticks_are_counted_by_worker_and_outcome()
    {
        using var metrics = new WorkflowMetrics();
        var services = new ServiceCollection();
        services.AddSingleton(metrics);
        IServiceScopeFactory scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var recorded = new List<(string Worker, string Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "wbskt_workflow_worker_ticks_total" && instrument.Meter.Name == WorkflowMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var byKey = tags.ToArray().ToDictionary(t => t.Key, t => (string)t.Value!);
            recorded.Add((byKey["worker"], byKey["outcome"]));
        });
        listener.Start();

        await new CountingWorker(new FixedLeadershipGate(true), scopes).TickAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CountingWorker(new FixedLeadershipGate(true), scopes, fail: true).TickAsync(CancellationToken.None));

        Assert.Contains(("Counting worker", "ok"), recorded);
        Assert.Contains(("Counting worker", "failed"), recorded);
    }

    private sealed class ScopedMarker;

    private sealed class CountingWorker(ILeadershipGate leadership, IServiceScopeFactory scopes, bool fail = false)
        : LeaderPeriodicWorker(leadership, scopes, NullLogger.Instance)
    {
        private readonly SemaphoreSlim _ran = new(0);

        public int Runs { get; private set; }
        public List<ScopedMarker> Markers { get; } = [];

        protected override string Name => "Counting worker";

        protected override TimeSpan Interval => TimeSpan.FromMilliseconds(10);

        protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
        {
            Runs++;
            if (services.GetService<ScopedMarker>() is { } marker)
            {
                Markers.Add(marker);
            }

            _ran.Release();
            return fail ? Task.FromException(new InvalidOperationException("tick failed")) : Task.CompletedTask;
        }

        public async Task WaitForRunsAsync(int count, CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                await _ran.WaitAsync(ct);
            }
        }
    }

    private sealed class FixedLeadershipGate(bool isActive) : ILeadershipGate
    {
        public bool IsActive => isActive;
    }
}
