using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IBookmarkProvider
{
    Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct);
    Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeysAsync(IReadOnlyCollection<string> matchKeys, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Array.Empty<BookmarkRow>());
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct);

    /// <summary>Atomically claims (deletes) the bookmark by RefId. Returns false if it was already claimed/gone - the caller lost the race.</summary>
    Task<bool> TryClaimAsync(Guid refId, CancellationToken ct);

    /// <summary>Atomically claims (deletes) up to <paramref name="batchSize"/> due bookmarks and returns the claimed rows.</summary>
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
