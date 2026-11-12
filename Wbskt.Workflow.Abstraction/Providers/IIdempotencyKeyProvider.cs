using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IIdempotencyKeyProvider
{
    Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct);
    Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct);
    Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct);
    Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct);

    /// <summary>
    /// Reclaims a 'Failed' claim row back to 'Pending' under a new claim token, atomically. Used when
    /// a previous claimant crashed or errored mid-resume so the event was never actually delivered -
    /// without this, the stale 'Failed' row would wedge the key as permanently claimed. Returns the
    /// current row regardless of whether this caller won the reclaim race (check BranchRefId == newBranchRefId).
    /// </summary>
    Task<IdempotencyKeyRow> ReclaimFailedAsync(string keyValue, Guid newBranchRefId, CancellationToken ct);

    /// <summary>
    /// Settles an event claim as Succeeded, only while <paramref name="claimToken"/> still holds it: an
    /// engine whose claim was released and taken over by a redelivery must not overwrite the new holder's.
    /// </summary>
    Task<IdempotencyKeyRow> MarkClaimSucceededAsync(string keyValue, Guid claimToken, string resultJson, CancellationToken ct);

    /// <summary>Settles an event claim as Failed, only while <paramref name="claimToken"/> still holds it.</summary>
    Task<IdempotencyKeyRow> MarkClaimFailedAsync(string keyValue, Guid claimToken, string errorJson, CancellationToken ct);

    /// <summary>
    /// Records on a Pending inbound-event claim that the event had waiting branches to wake
    /// (<see cref="IdempotencyClaimResults.Resumed"/>), so a redelivery that reclaims it after a stop does
    /// not take it for an event that only starts trigger runs.
    /// </summary>
    Task MarkResumedAsync(string keyValue, Guid claimToken, CancellationToken ct);

    /// <summary>
    /// Marks every Pending inbound-event and trigger-start claim Failed, so a redelivery of its event can
    /// reclaim it, then settles every unsettled trigger-start claim whose run exists, since recovery runs
    /// that run. Only a new leader calls it, before its bus starts, when any Pending claim was left by an
    /// engine that stopped mid-dispatch. Returns the number marked Failed.
    /// </summary>
    Task<int> ReleaseAbandonedClaimsAsync(CancellationToken ct);

    /// <summary>Deletes up to <paramref name="batchSize"/> idempotency rows created before the cutoff. Returns the count deleted.</summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);
}
