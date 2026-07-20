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

    /// <summary>Deletes up to <paramref name="batchSize"/> idempotency rows created before the cutoff. Returns the count deleted.</summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);
}
