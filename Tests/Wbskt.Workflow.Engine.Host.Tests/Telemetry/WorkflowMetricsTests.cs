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
            if (instrument.Meter.Name == WorkflowMetrics.MeterName && instrument.Name == "wbskt_workflow_runs_completed_total")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "wbskt_workflow_runs_completed_total")
            {
                total += measurement;
            }
        });
        listener.Start();

        metrics.RunsCompleted.Add(1);
        metrics.RunsCompleted.Add(2);

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
            if (instrument.Meter.Name == WorkflowMetrics.MeterName && instrument.Name == "wbskt_workflow_node_duration_ms")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "wbskt_workflow_node_duration_ms")
            {
                recorded = measurement;
            }
        });
        listener.Start();

        metrics.NodeDuration.Record(12.5d);

        Assert.Equal(12.5d, recorded);
    }
}
