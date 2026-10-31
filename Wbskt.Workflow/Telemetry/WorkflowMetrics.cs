using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Workflow.Telemetry;

public sealed class WorkflowMetrics : IWorkerTickMetrics, IDisposable
{
    public const string MeterName = "Wbskt.Workflow";
    private readonly Meter _meter;

    private long _activeBranches;
    private long _dispatcherQueueDepth;
    private long _pendingTriggerDepth;
    private long _overdueBookmarks;
    private readonly ConcurrentDictionary<string, long> _bookmarksByWakeKind = new(StringComparer.OrdinalIgnoreCase);

    public WorkflowMetrics()
    {
        _meter = new Meter(MeterName);
        RunsStarted = _meter.CreateCounter<long>("wbskt_workflow_runs_started_total");
        RunsCompleted = _meter.CreateCounter<long>("wbskt_workflow_runs_completed_total");
        RunsRefused = _meter.CreateCounter<long>("wbskt_workflow_runs_refused_total");
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

        // The backlog that matters: wakes that are due and nobody has claimed. Outstanding bookmarks
        // above include every run legitimately waiting days for an approval, so their count alone
        // says nothing about whether the engine is keeping up.
        BookmarksOverdue = _meter.CreateObservableGauge("wbskt_workflow_bookmarks_overdue", () => { return _overdueBookmarks; });

        NodeDuration = _meter.CreateHistogram<double>("wbskt_workflow_node_duration_ms");
        DispatcherQueueDepth = _meter.CreateObservableGauge("wbskt_workflow_dispatcher_queue_depth", () => { return _dispatcherQueueDepth; });
        CreditsConsumed = _meter.CreateCounter<double>("wbskt_workflow_credits_consumed_total");
        WorkerTicks = _meter.CreateCounter<long>("wbskt_workflow_worker_ticks_total");
        InboundEvents = _meter.CreateCounter<long>("wbskt_workflow_inbound_events_total");

        PendingTriggers = _meter.CreateObservableGauge("wbskt_workflow_pending_triggers", () =>
        {
            return new[] { new Measurement<long>(_pendingTriggerDepth, new KeyValuePair<string, object?>("policy", "Queue")) };
        });
    }

    public Counter<long> RunsStarted { get; }
    public Counter<long> RunsCompleted { get; }

    /// <summary>Triggers that started no run because their workspace was at its active-run ceiling.</summary>
    public Counter<long> RunsRefused { get; }
    public ObservableGauge<long> BranchesActive { get; }
    public ObservableGauge<long> BookmarksOutstanding { get; }
    public ObservableGauge<long> BookmarksOverdue { get; }
    public Histogram<double> NodeDuration { get; }
    public ObservableGauge<long> DispatcherQueueDepth { get; }
    public Counter<double> CreditsConsumed { get; }

    /// <summary>Ticks of the periodic background workers, tagged <c>worker</c> and <c>outcome</c> (ok or failed).</summary>
    public Counter<long> WorkerTicks { get; }

    /// <summary>Every inbound event the engine handled, tagged <c>channel</c> and <c>outcome</c> (the dispatch outcome, or Failed). A retried message counts once per attempt.</summary>
    public Counter<long> InboundEvents { get; }
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

    public void UpdateOverdueBookmarks(long overdue)
    {
        _overdueBookmarks = overdue;
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

    public void RecordRunRefused(string workflowRef, string triggerKind, int workspaceId)
    {
        RunsRefused.Add(
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

    public void RecordWorkerTick(string worker, string outcome)
    {
        WorkerTicks.Add(1, new TagList { { "worker", worker }, { "outcome", outcome } });
    }

    public void RecordInboundEvent(string channel, string outcome)
    {
        InboundEvents.Add(1, new TagList { { "channel", channel }, { "outcome", outcome } });
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
