using System.Diagnostics.Metrics;

namespace Wbskt.Workflow.Telemetry;

public sealed class WorkflowMetrics : IDisposable
{
    public const string MeterName = "Wbskt.Workflow";
    private readonly Meter _meter;
    private long _activeRuns;
    private long _activeBranches;
    private long _parkedBookmarks;
    private long _pendingTriggerDepth;

    public WorkflowMetrics()
    {
        _meter = new Meter(MeterName);
        ActiveRunsGauge = _meter.CreateObservableGauge("workflow.active_runs", () => _activeRuns);
        ActiveBranchesGauge = _meter.CreateObservableGauge("workflow.active_branches", () => _activeBranches);
        ParkedBookmarksGauge = _meter.CreateObservableGauge("workflow.parked_bookmarks", () => _parkedBookmarks);
        PendingTriggerDepthGauge = _meter.CreateObservableGauge("workflow.pending_trigger_depth", () => _pendingTriggerDepth);
        CompletedRunsCounter = _meter.CreateCounter<long>("workflow.completed_runs");
        DispatchedBranchesCounter = _meter.CreateCounter<long>("workflow.dispatched_branches");
        AppendedHistoryEventsCounter = _meter.CreateCounter<long>("workflow.appended_history_events");
        NodeExecutionsCounter = _meter.CreateCounter<long>("workflow.node_executions");
        NodeDurationHistogram = _meter.CreateHistogram<double>("workflow.node.duration");
    }

    public ObservableGauge<long> ActiveRunsGauge { get; }
    public ObservableGauge<long> ActiveBranchesGauge { get; }
    public ObservableGauge<long> ParkedBookmarksGauge { get; }
    public ObservableGauge<long> PendingTriggerDepthGauge { get; }
    public Counter<long> CompletedRunsCounter { get; }
    public Counter<long> DispatchedBranchesCounter { get; }
    public Counter<long> AppendedHistoryEventsCounter { get; }
    public Counter<long> NodeExecutionsCounter { get; }
    public Histogram<double> NodeDurationHistogram { get; }

    public void UpdateSnapshot(long activeRuns, long activeBranches, long parkedBookmarks, long pendingTriggerDepth)
    {
        _activeRuns = activeRuns;
        _activeBranches = activeBranches;
        _parkedBookmarks = parkedBookmarks;
        _pendingTriggerDepth = pendingTriggerDepth;
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
