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

    // Every run-level metric carries workspace_id. Without it these series can only be read as a
    // fleet total: an operator cannot answer "how much is this tenant using" or "is one workspace
    // responsible for the failure spike", which are the first two questions asked of them.
    //
    // node_id is deliberately NOT a tag on NodeDuration - one series per node per workflow is
    // unbounded cardinality. Per-node timings come from the history stream instead, aggregated by
    // Run_GetNodeTimingsBy_WorkflowRefId, which is scoped to one workflow and one window.
    public void RecordRunStarted(string workflowRef, string triggerKind, int workspaceId)
    {
        RunsStarted.Add(
            1,
            new KeyValuePair<string, object?>("workflow_ref", workflowRef),
            new KeyValuePair<string, object?>("trigger_kind", triggerKind),
            new KeyValuePair<string, object?>("workspace_id", workspaceId));
    }

    public void RecordRunCompleted(string workflowRef, string status, int workspaceId)
    {
        RunsCompleted.Add(
            1,
            new KeyValuePair<string, object?>("workflow_ref", workflowRef),
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("workspace_id", workspaceId));
    }

    public void RecordNodeDuration(string nodeKind, string outcome, double durationMs)
    {
        NodeDuration.Record(durationMs, new KeyValuePair<string, object?>("node_kind", nodeKind), new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordCreditsConsumed(string workflowRef, double cost, int workspaceId)
    {
        CreditsConsumed.Add(
            cost,
            new KeyValuePair<string, object?>("workflow_ref", workflowRef),
            new KeyValuePair<string, object?>("workspace_id", workspaceId));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
