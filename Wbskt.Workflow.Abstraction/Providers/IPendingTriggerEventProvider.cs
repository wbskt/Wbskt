using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IPendingTriggerEventProvider
{
    /// <summary>
    /// Enqueues while a run is still active for the key or earlier events still wait on it, checked under
    /// the lock the drain takes, so the event cannot land just after the run's finalizer found the queue
    /// empty (#470) and cannot start ahead of events queued before it. Null when neither holds: the caller
    /// starts a run instead.
    /// </summary>
    Task<PendingTriggerEventRow?> EnqueueBehindAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, long? exceptPendingEventId, CancellationToken ct);
    // Dequeue is always scoped by trigger node as well as workflow + correlation key: two triggers in
    // one workflow can share a correlation key, and draining across them would start a run from the
    // wrong trigger with the wrong payload.
    // Leases the queue's head, or returns null when the queue is empty or its head is still leased. The
    // row stays until DeleteByIdAsync, so a replay that fails is leased again once the lease runs out.
    Task<PendingTriggerEventRow?> LeaseNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, int leaseSeconds, CancellationToken ct);
    Task DeleteByIdAsync(long id, CancellationToken ct);
    // Keys with a queued, unleased event and no run going, so no finalizer will drain them.
    Task<IReadOnlyCollection<PendingTriggerEventKey>> GetIdleKeysAsync(int batchSize, CancellationToken ct);
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);
    Task<long> CountAllAsync(CancellationToken ct);
}
