using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Host.Tests.Runtime;

internal sealed class RecordingOutcomeProvider(bool fail = false) : ITriggerDispatchOutcomeProvider
{
    private readonly List<TriggerDispatchOutcomeRow> _records = [];

    public Wbskt.Workflow.Runtime.TriggerOutcomeBuffer Buffer { get; } = new();

    /// <summary>What was written, after writing whatever is still buffered, as the engine's flusher would.</summary>
    public List<TriggerDispatchOutcomeRow> Records
    {
        get
        {
            _records.AddRange(Buffer.Drain());
            return _records;
        }
    }

    public Task RecordManyAsync(IReadOnlyList<TriggerDispatchOutcomeRow> rows, CancellationToken ct)
    {
        if (fail)
        {
            throw new InvalidOperationException("database unavailable");
        }

        _records.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<TriggerDispatchOutcomeRow>> GetRecentAsync(Guid workflowRefId, Guid triggerNodeId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<TriggerDispatchOutcomeRow>>(Records.Where(r => r.WorkflowRefId == workflowRefId && r.TriggerNodeId == triggerNodeId).Reverse().Take(take).ToArray());

    public Task<TriggerHoldStateRow?> FindHoldStateAsync(Guid workflowRefId, Guid triggerNodeId, CancellationToken ct) =>
        Task.FromResult<TriggerHoldStateRow?>(null);

    public static Wbskt.Workflow.Runtime.TriggerOutcomeLog Log(RecordingOutcomeProvider provider) =>
        new(provider.Buffer, TimeProvider.System);
}
