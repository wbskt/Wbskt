using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IBookmarkProvider
{
    Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct);
    Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task<BookmarkRow?> GetByIdAsync(long bookmarkId, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeysAsync(IReadOnlyCollection<string> matchKeys, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Array.Empty<BookmarkRow>());
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct);
    Task DeleteAsync(Guid refId, CancellationToken ct);
    Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct);
    Task<long> CountAsync(CancellationToken ct);
    Task<int> DeleteOrphansAsync(CancellationToken ct);
    Task DeleteAllByRunIdAsync(int runId, CancellationToken ct);
    Task<IReadOnlyDictionary<string, long>> CountGroupedByWakeKindAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());
}
