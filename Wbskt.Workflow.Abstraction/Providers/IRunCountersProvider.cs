namespace Wbskt.Workflow.Abstraction.Providers;

using Entities;

public interface IRunCountersProvider
{
    Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct);
    Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct);
    Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct);
    /// <summary>Charges the run, or returns null when the charge would take it past its budget.</summary>
    Task<CreditCharge?> TryChargeAsync(int runId, decimal cost, CancellationToken ct);
    Task<long> SumActiveBranchesAsync(CancellationToken ct);
}
