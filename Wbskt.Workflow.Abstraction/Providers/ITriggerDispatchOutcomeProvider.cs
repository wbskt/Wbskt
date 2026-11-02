using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

/// <summary>
/// The latest outcomes of each workflow trigger, kept so "I sent the message and nothing happened"
/// has an answer. Only the newest <see cref="Keep"/> are kept per trigger node.
/// </summary>
public interface ITriggerDispatchOutcomeProvider
{
    /// <summary>How many outcomes are kept for each trigger node.</summary>
    const int Keep = 20;

    /// <summary>Writes outcomes in the order given, keeping the newest <see cref="Keep"/> per trigger node.</summary>
    Task RecordManyAsync(IReadOnlyList<TriggerDispatchOutcomeRow> rows, CancellationToken ct);

    /// <summary>The newest outcomes for one trigger node, newest first.</summary>
    Task<IReadOnlyCollection<TriggerDispatchOutcomeRow>> GetRecentAsync(Guid workflowRefId, Guid triggerNodeId, int take, CancellationToken ct);

    /// <summary>The hold state of a client trigger with a hold time on the workflow's enabled version; null when it has none.</summary>
    Task<TriggerHoldStateRow?> FindHoldStateAsync(Guid workflowRefId, Guid triggerNodeId, CancellationToken ct);
}
