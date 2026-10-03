namespace Wbskt.Workflow.Abstraction.Providers;

public interface IRunRetentionProvider
{
    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> runs that completed before <paramref name="cutoffUtc"/>,
    /// with their branches, history and other per-run rows. Returns how many runs were deleted.
    /// </summary>
    Task<int> DeleteRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);
}
