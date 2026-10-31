using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Host.Tests.Runtime;

internal sealed class RecordingOutcomeProvider(bool fail = false) : ITriggerDispatchOutcomeProvider
{
    public List<TriggerDispatchOutcomeRow> Records { get; } = [];

    public Task RecordAsync(TriggerDispatchOutcomeRow row, CancellationToken ct)
    {
        if (fail)
        {
            throw new InvalidOperationException("database unavailable");
        }

        Records.Add(row);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<TriggerDispatchOutcomeRow>> GetRecentAsync(Guid workflowRefId, Guid triggerNodeId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<TriggerDispatchOutcomeRow>>(Records.Where(r => r.WorkflowRefId == workflowRefId && r.TriggerNodeId == triggerNodeId).Reverse().Take(take).ToArray());

    public Task<TriggerHoldStateRow?> FindHoldStateAsync(Guid workflowRefId, Guid triggerNodeId, CancellationToken ct) =>
        Task.FromResult<TriggerHoldStateRow?>(null);

    public static Wbskt.Workflow.Runtime.TriggerOutcomeLog Log(RecordingOutcomeProvider provider) =>
        new(provider, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Wbskt.Workflow.Runtime.TriggerOutcomeLog>.Instance);
}
