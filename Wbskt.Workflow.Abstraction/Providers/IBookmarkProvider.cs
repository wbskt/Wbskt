using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IBookmarkProvider
{
    Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct);
    Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> GetDueAsync(DateTime now, int batchSize, CancellationToken ct);
    Task DeleteAsync(Guid refId, CancellationToken ct);
    Task DeleteAllByRunIdAsync(int runId, CancellationToken ct);
}
