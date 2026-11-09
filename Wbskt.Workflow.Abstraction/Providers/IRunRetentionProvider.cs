using Wbskt.Models.Billing;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IRunRetentionProvider
{
    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> finished runs, with their branches, history and other
    /// per-run rows: those past their workspace plan's <paramref name="retention"/> counted back from
    /// <paramref name="nowUtc"/> (or the tenant's override), and any that completed before
    /// <paramref name="cutoffUtc"/>. Returns how many runs were deleted.
    /// </summary>
    Task<int> DeleteRetiredRunsAsync(DateTime nowUtc, IReadOnlyCollection<RunHistoryRetention> retention, DateTime cutoffUtc, int batchSize, CancellationToken ct);
}

/// <summary>How many days <paramref name="PlanCode"/> keeps a finished run (<see cref="PlanLimits.RunHistoryDays"/>).</summary>
public sealed record RunHistoryRetention(PlanCode PlanCode, int Days);
