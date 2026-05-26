using System.Diagnostics.Metrics;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.Tests.Telemetry;

public sealed class WorkflowMetricsTests
{
    [Fact]
    public void Meter_name_is_correct()
    {
        Assert.Equal("Wbskt.Workflow", WorkflowMetrics.MeterName);
    }

    [Fact]
    public void Counter_increments_tracked()
    {
        using var metrics = new WorkflowMetrics();
        long total = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == WorkflowMetrics.MeterName && instrument.Name == "workflow.completed_runs")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "workflow.completed_runs")
            {
                total += measurement;
            }
        });
        listener.Start();

        metrics.CompletedRunsCounter.Add(1);
        metrics.CompletedRunsCounter.Add(2);

        Assert.Equal(3, total);
    }

    [Fact]
    public void Histogram_records_value()
    {
        using var metrics = new WorkflowMetrics();
        double? recorded = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == WorkflowMetrics.MeterName && instrument.Name == "workflow.node.duration")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "workflow.node.duration")
            {
                recorded = measurement;
            }
        });
        listener.Start();

        metrics.NodeDurationHistogram.Record(12.5d);

        Assert.Equal(12.5d, recorded);
    }
}

