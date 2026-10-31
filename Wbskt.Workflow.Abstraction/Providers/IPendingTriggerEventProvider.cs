using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IPendingTriggerEventProvider
{
    /// <summary>
    /// Enqueues only while a run is still active for the key, checked under the lock the drain takes, so
    /// the event cannot land just after the run's finalizer found the queue empty (#470). Null when no run
    /// is active any more: the caller starts one instead.
    /// </summary>
    Task<PendingTriggerEventRow?> EnqueueBehindActiveRunAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct);
    // Dequeue is always scoped by trigger node as well as workflow + correlation key: two triggers in
    // one workflow can share a correlation key, and draining across them would start a run from the
    // wrong trigger with the wrong payload.
    Task<PendingTriggerEventRow?> DequeueNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct);
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);
    Task<long> CountAllAsync(CancellationToken ct);
}
