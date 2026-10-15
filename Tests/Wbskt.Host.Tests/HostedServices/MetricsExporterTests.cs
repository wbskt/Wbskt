using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Host.Tests.HostedServices;

public sealed class MetricsExporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Overdue_bookmarks_are_counted_with_a_minute_of_grace()
    {
        var bookmarks = new Mock<IBookmarkProvider>();
        DateTime? cutoff = null;
        bookmarks
            .Setup(b => b.CountOverdueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, CancellationToken>((c, _) => cutoff = c)
            .ReturnsAsync(3);
        bookmarks
            .Setup(b => b.CountGroupedByWakeKindAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, long>());
        using var metrics = new WorkflowMetrics();
        var exporter = CreateExporter(new FixedLeadershipGate(true), metrics, bookmarks.Object);

        await exporter.TickAsync(CancellationToken.None);

        Assert.Equal(Now.UtcDateTime.AddMinutes(-1), cutoff);
        Assert.Contains(3L, Observe(metrics, "wbskt_workflow_bookmarks_overdue"));
    }

    [Fact]
    public async Task A_standby_does_not_poll_or_publish()
    {
        // The standby holds no work: polling from it doubled the database load and published a
        // dispatcher depth of zero under the leader's metric names.
        var bookmarks = new Mock<IBookmarkProvider>(MockBehavior.Strict);
        var counters = new Mock<IRunCountersProvider>(MockBehavior.Strict);
        var pending = new Mock<IPendingTriggerEventProvider>(MockBehavior.Strict);
        using var metrics = new WorkflowMetrics();
        var exporter = new MetricsExporter(
            new FixedClock(Now),
            new FixedLeadershipGate(false),
            WorkerTestServices.Scopes(counters.Object, bookmarks.Object, pending.Object),
            metrics,
            new ChannelRunDispatcher(),
            NullLogger<MetricsExporter>.Instance,
            WorkerTestServices.Options());

        await exporter.TickAsync(CancellationToken.None);

        bookmarks.VerifyNoOtherCalls();
        counters.VerifyNoOtherCalls();
        pending.VerifyNoOtherCalls();
    }

    private static MetricsExporter CreateExporter(ILeadershipGate leadership, WorkflowMetrics metrics, IBookmarkProvider bookmarks) =>
        new(
            new FixedClock(Now),
            leadership,
            WorkerTestServices.Scopes(Mock.Of<IRunCountersProvider>(), bookmarks, Mock.Of<IPendingTriggerEventProvider>()),
            metrics,
            new ChannelRunDispatcher(),
            NullLogger<MetricsExporter>.Instance,
            WorkerTestServices.Options());

    private sealed class FixedLeadershipGate(bool isActive) : ILeadershipGate
    {
        public bool IsActive => isActive;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Every WorkflowMetrics shares the meter name, and tests running in parallel keep their own alive,
    // so the listener sees one reading per live instance. This test's reading is among them.
    private static List<long> Observe(WorkflowMetrics metrics, string instrument)
    {
        var values = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) =>
        {
            if (i.Meter.Name == WorkflowMetrics.MeterName && i.Name == instrument)
            {
                l.EnableMeasurementEvents(i);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => values.Add(measurement));
        listener.Start();
        listener.RecordObservableInstruments();
        GC.KeepAlive(metrics);
        return values;
    }
}
