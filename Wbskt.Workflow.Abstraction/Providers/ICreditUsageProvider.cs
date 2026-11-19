using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

/// <summary>The ledger of workflow credits each workspace spent a day (<c>dbo.WorkspaceCreditUsage</c>).</summary>
public interface ICreditUsageProvider
{
    /// <summary>
    /// Moves what up to <paramref name="batchSize"/> runs consumed since they last posted into the ledger
    /// on the UTC day of <paramref name="nowUtc"/>, whether the runs are still going or ended, and draws
    /// what is past the allowance from the credit grants live then. Returns what each workspace posted.
    /// </summary>
    Task<IReadOnlyCollection<WorkspaceCreditPost>> PostAsync(DateTime nowUtc, int batchSize, CancellationToken ct);

    /// <summary>
    /// The caps and plan allowances whose spend in the month starting <paramref name="monthStart"/> has
    /// reached a threshold not announced yet.
    /// </summary>
    Task<IReadOnlyCollection<CreditNotice>> GetDueNoticesAsync(DateOnly monthStart, CancellationToken ct);

    /// <summary>Records that <paramref name="notice"/>'s threshold <paramref name="percent"/> was announced for the month starting <paramref name="monthStart"/>.</summary>
    Task MarkNotifiedAsync(CreditNotice notice, DateOnly monthStart, int percent, CancellationToken ct);
}
