using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IBookmarkProvider
{
    Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct);
    Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeysAsync(IReadOnlyCollection<string> matchKeys, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Array.Empty<BookmarkRow>());
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct);

    /// <summary>
    /// Atomically claims (deletes) the bookmark by RefId, and in the same transaction marks its branch woken
    /// (so a new leader resumes it if the dispatch is lost). Returns false if it was already claimed/gone -
    /// the caller lost the race, and nothing is written.
    /// </summary>
    Task<bool> TryClaimAsync(Guid refId, CancellationToken ct);

    /// <summary>
    /// <see cref="TryClaimAsync(Guid, CancellationToken)"/>, and when this call wins, stores
    /// <paramref name="wakeLocalJson"/> (the branch's local state with the wake merged in) on the
    /// bookmark's branch in the same transaction, so a wake that is claimed is never lost before it is
    /// stored. A loser writes nothing.
    /// </summary>
    /// <remarks>The default throws: a double that never resumes on an inbound event needs no stub, and
    /// one that does must store the wake rather than drop it.</remarks>
    Task<bool> TryClaimWithWakeAsync(Guid refId, string wakeLocalJson, CancellationToken ct)
        => throw new NotSupportedException("This bookmark provider does not store a wake with its claim.");

    /// <summary>
    /// Atomically claims (deletes) up to <paramref name="batchSize"/> due bookmarks and returns the claimed
    /// rows. In the same transaction each claimed branch is marked woken, and a TTL on a wait that is not a
    /// timer sets the branch's <see cref="Entities.BranchRow.PendingTakePort"/> to the bookmark's TTL port.
    /// </summary>
    Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct);

    Task DeleteAsync(Guid refId, CancellationToken ct);
    Task<long> CountAsync(CancellationToken ct);
    Task<int> DeleteOrphansAsync(CancellationToken ct);
    Task DeleteAllByRunIdAsync(int runId, CancellationToken ct);
    Task<IReadOnlyDictionary<string, long>> CountGroupedByWakeKindAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());

    /// <summary>Bookmarks that fell due at or before <paramref name="cutoffUtc"/> and have not been claimed.</summary>
    Task<long> CountOverdueAsync(DateTime cutoffUtc, CancellationToken ct) => Task.FromResult(0L);
}
