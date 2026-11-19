namespace Wbskt.Workflow.Abstraction.Providers;

using Entities;

public interface IRunCountersProvider
{
    Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct);
    Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct);
    Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct);
    /// <summary>
    /// Charges the run. A charge that would take it past a budget is refused, with <see cref="CreditCharge.Refusal"/>
    /// saying which; null is read as a refusal for the run's own budget.
    /// </summary>
    Task<CreditCharge?> TryChargeAsync(int runId, decimal cost, CancellationToken ct);
    Task<long> SumActiveBranchesAsync(CancellationToken ct);
}
