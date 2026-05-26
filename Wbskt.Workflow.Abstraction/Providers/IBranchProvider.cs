using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IBranchProvider
{
    Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct);
    Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct);
    Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct);
    Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct);
}
