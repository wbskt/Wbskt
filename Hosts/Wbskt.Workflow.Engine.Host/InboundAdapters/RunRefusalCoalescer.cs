using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Gathers the triggers the workspace's active-run ceiling refuses into one
/// <see cref="WorkflowTriggerRefusedEvent"/> per workflow per <see cref="Window"/>, from the first
/// refusal on. <see cref="HostedServices.RunRefusalPublisher"/> publishes each once its window is over.
/// </summary>
/// <remarks>
/// In memory and per process: each engine replica reports what it refused, and a crash loses at most
/// one window's warning. The refusals themselves are in <c>wbskt_workflow_runs_refused_total</c>.
/// </remarks>
public sealed class RunRefusalCoalescer(TimeSpan window) : IRunRefusalReporter
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<(int WorkspaceId, Guid WorkflowRefId), Bucket> _buckets = [];

    public RunRefusalCoalescer() : this(DefaultWindow)
    {
    }

    public TimeSpan Window { get; } = window;

    public void Report(RunRefusal refusal)
    {
        lock (_gate)
        {
            var key = (refusal.WorkspaceId, refusal.WorkflowRefId);
            if (!_buckets.TryGetValue(key, out Bucket? bucket))
            {
                _buckets[key] = bucket = new Bucket(refusal.AtUtc);
            }

            bucket.Add(refusal);
        }
    }

    /// <summary>The warnings whose window has ended by <paramref name="nowUtc"/>, or every one with <paramref name="all"/>.</summary>
    public IReadOnlyList<WorkflowTriggerRefusedEvent> TakeDue(DateTime nowUtc, bool all = false)
    {
        lock (_gate)
        {
            var due = _buckets.Where(b => all || b.Value.FirstAtUtc + Window <= nowUtc).ToList();
            foreach (var (key, _) in due)
            {
                _buckets.Remove(key);
            }

            return due.Select(b => b.Value.ToEvent(b.Key.WorkspaceId, b.Key.WorkflowRefId)).ToList();
        }
    }

    private sealed class Bucket(DateTime firstAtUtc)
    {
        private readonly SortedSet<string> _kinds = new(StringComparer.Ordinal);
        private int _count;
        private int _workflowId;
        private int _ceiling;
        private DateTime _lastAtUtc = firstAtUtc;

        public DateTime FirstAtUtc { get; private set; } = firstAtUtc;

        public void Add(RunRefusal refusal)
        {
            _count++;
            _kinds.Add(refusal.TriggerKind);
            _workflowId = refusal.WorkflowDefinitionId;
            _ceiling = refusal.Ceiling;
            // Events can be handled out of order; the window still runs from the first one seen.
            if (refusal.AtUtc < FirstAtUtc)
            {
                FirstAtUtc = refusal.AtUtc;
            }

            if (refusal.AtUtc > _lastAtUtc)
            {
                _lastAtUtc = refusal.AtUtc;
            }
        }

        public WorkflowTriggerRefusedEvent ToEvent(int workspaceId, Guid workflowRefId) =>
            new(workflowRefId, _workflowId, workspaceId, _count, FirstAtUtc, _lastAtUtc, string.Join(",", _kinds), _ceiling);
    }
}
