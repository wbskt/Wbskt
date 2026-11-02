using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Trigger outcomes waiting to be written, kept off the dispatch path. Only the newest
/// <see cref="ITriggerDispatchOutcomeProvider.Keep"/> per trigger are held, since the table keeps no
/// more, so a chatty device costs memory and writes bounded by its triggers, not its messages.
/// </summary>
public sealed class TriggerOutcomeBuffer
{
    private readonly object _gate = new();
    private Dictionary<(Guid WorkflowRefId, Guid TriggerNodeId), Queue<TriggerDispatchOutcomeRow>> _pending = [];

    public void Add(TriggerDispatchOutcomeRow row)
    {
        lock (_gate)
        {
            var key = (row.WorkflowRefId, row.TriggerNodeId);
            if (!_pending.TryGetValue(key, out var rows))
            {
                rows = new Queue<TriggerDispatchOutcomeRow>();
                _pending[key] = rows;
            }

            if (rows.Count == ITriggerDispatchOutcomeProvider.Keep)
            {
                rows.Dequeue();
            }

            rows.Enqueue(row);
        }
    }

    /// <summary>Takes everything waiting, oldest first within each trigger.</summary>
    public IReadOnlyList<TriggerDispatchOutcomeRow> Drain()
    {
        Dictionary<(Guid, Guid), Queue<TriggerDispatchOutcomeRow>> taken;
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return [];
            }

            taken = _pending;
            _pending = [];
        }

        return taken.Values.SelectMany(rows => rows).ToList();
    }
}
