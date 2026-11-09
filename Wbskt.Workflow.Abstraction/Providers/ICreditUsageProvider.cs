namespace Wbskt.Workflow.Abstraction.Providers;

/// <summary>The ledger of workflow credits each workspace spent a day (<c>dbo.WorkspaceCreditUsage</c>).</summary>
public interface ICreditUsageProvider
{
    /// <summary>
    /// Moves what up to <paramref name="batchSize"/> runs consumed since they last posted into the ledger
    /// on <paramref name="day"/>: the runs still going, and those that ended since <paramref name="endedSinceUtc"/>.
    /// Returns how many runs posted.
    /// </summary>
    Task<int> PostAsync(DateOnly day, DateTime endedSinceUtc, int batchSize, CancellationToken ct);
}
