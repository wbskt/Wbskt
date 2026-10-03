namespace Wbskt.Auth.Host.Providers;

public interface ICredentialRetentionProvider
{
    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> rows per credential table that expired before
    /// <paramref name="cutoffUtc"/>; returns the total deleted.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default);
}
