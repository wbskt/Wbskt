namespace Wbskt.Workflow.Abstraction.Providers;

public interface IRunCountersProvider
{
    Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct);
    Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct);
    Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct);
}
