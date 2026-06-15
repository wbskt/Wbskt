using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Wbskt.Workflow.Telemetry;

public sealed class WorkflowMetrics : IDisposable
{
    public const string MeterName = "Wbskt.Workflow";
    private readonly Meter _meter;

    private long _activeBranches;
    private long _dispatcherQueueDepth;
    private long _pendingTriggerDepth;
    private readonly ConcurrentDictionary<string, long> _bookmarksByWakeKind = new(StringComparer.OrdinalIgnoreCase);

    public WorkflowMetrics()
    {
        _meter = new Meter(MeterName);
        RunsStarted = _meter.CreateCounter<long>("wbskt_workflow_runs_started_total");
        RunsCompleted = _meter.CreateCounter<long>("wbskt_workflow_runs_completed_total");
        BranchesActive = _meter.CreateObservableGauge("wbskt_workflow_branches_active", () => { return _activeBranches; });
        
        BookmarksOutstanding = _meter.CreateObservableGauge("wbskt_workflow_bookmarks_outstanding", () =>
        {
            List<Measurement<long>> measurements = new();
            foreach (KeyValuePair<string, long> kvp in _bookmarksByWakeKind)
            {
                measurements.Add(new Measurement<long>(kvp.Value, new KeyValuePair<string, object?>("wake_kind", kvp.Key)));
            }
            return measurements;
        });

        NodeDuration = _meter.CreateHistogram<double>("wbskt_workflow_node_duration_ms");
        DispatcherQueueDepth = _meter.CreateObservableGauge("wbskt_workflow_dispatcher_queue_depth", () => { return _dispatcherQueueDepth; });
        CreditsConsumed = _meter.CreateCounter<double>("wbskt_workflow_credits_consumed_total");
        FlusherLag = _meter.CreateObservableGauge("wbskt_workflow_history_event_flusher_lag", () => { return 0L; });

        PendingTriggers = _meter.CreateObservableGauge("wbskt_workflow_pending_triggers", () =>
        {
            return new[] { new Measurement<long>(_pendingTriggerDepth, new KeyValuePair<string, object?>("policy", "Queue")) };
        });
    }

    public Counter<long> RunsStarted { get; }
    public Counter<long> RunsCompleted { get; }
    public ObservableGauge<long> BranchesActive { get; }
    public ObservableGauge<long> BookmarksOutstanding { get; }
    public Histogram<double> NodeDuration { get; }
    public ObservableGauge<long> DispatcherQueueDepth { get; }
    public Counter<double> CreditsConsumed { get; }
    public ObservableGauge<long> FlusherLag { get; }
    public ObservableGauge<long> PendingTriggers { get; }

    public void UpdateSnapshot(long activeBranches, long dispatcherQueueDepth, long pendingTriggerDepth, IReadOnlyDictionary<string, long> bookmarksByWakeKind)
    {
        _activeBranches = activeBranches;
        _dispatcherQueueDepth = dispatcherQueueDepth;
        _pendingTriggerDepth = pendingTriggerDepth;
        _bookmarksByWakeKind.Clear();
        foreach (KeyValuePair<string, long> kvp in bookmarksByWakeKind)
        {
            _bookmarksByWakeKind[kvp.Key] = kvp.Value;
        }
    }

    public void RecordRunStarted(string workflowRef, string triggerKind)
    {
        RunsStarted.Add(1, new KeyValuePair<string, object?>("workflow_ref", workflowRef), new KeyValuePair<string, object?>("trigger_kind", triggerKind));
    }

    public void RecordRunCompleted(string workflowRef, string status)
    {
        RunsCompleted.Add(1, new KeyValuePair<string, object?>("workflow_ref", workflowRef), new KeyValuePair<string, object?>("status", status));
    }

    public void RecordNodeDuration(string nodeKind, string outcome, double durationMs)
    {
        NodeDuration.Record(durationMs, new KeyValuePair<string, object?>("node_kind", nodeKind), new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordCreditsConsumed(string workflowRef, double cost)
    {
        CreditsConsumed.Add(cost, new KeyValuePair<string, object?>("workflow_ref", workflowRef));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
