namespace Wbskt.Workflow.Abstraction.Providers;

using Wbskt.Workflow.Abstraction.Entities;

public interface IRunCountersProvider
{
    Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct);
    Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct);
    Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct);
    Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct);
}
