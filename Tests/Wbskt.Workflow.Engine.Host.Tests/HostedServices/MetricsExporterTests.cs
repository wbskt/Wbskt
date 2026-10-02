using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class MetricsExporterTests
{
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
        var exporter = new MetricsExporter(
            Mock.Of<IRunProvider>(),
            Mock.Of<IRunCountersProvider>(),
            bookmarks.Object,
            Mock.Of<IPendingTriggerEventProvider>(),
            metrics,
            new ChannelRunDispatcher(),
            NullLogger<MetricsExporter>.Instance);

        await exporter.ProcessMetricsAsync(CancellationToken.None);

        Assert.NotNull(cutoff);
        Assert.InRange(DateTime.UtcNow - cutoff!.Value, TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(65));
        Assert.Equal(3, Observe(metrics, "wbskt_workflow_bookmarks_overdue"));
    }

    private static long Observe(WorkflowMetrics metrics, string instrument)
    {
        long value = -1;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) =>
        {
            if (i.Meter.Name == WorkflowMetrics.MeterName && i.Name == instrument)
            {
                l.EnableMeasurementEvents(i);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => value = measurement);
        listener.Start();
        listener.RecordObservableInstruments();
        GC.KeepAlive(metrics);
        return value;
    }
}
