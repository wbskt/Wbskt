using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

/// <summary>The ledger of workflow credits each workspace spent a day (<c>dbo.WorkspaceCreditUsage</c>).</summary>
public interface ICreditUsageProvider
{
    /// <summary>
    /// Moves what up to <paramref name="batchSize"/> runs consumed since they last posted into the ledger
    /// on <paramref name="day"/>, whether the runs are still going or ended. Returns what each workspace posted.
    /// </summary>
    Task<IReadOnlyCollection<WorkspaceCreditPost>> PostAsync(DateOnly day, int batchSize, CancellationToken ct);
}
