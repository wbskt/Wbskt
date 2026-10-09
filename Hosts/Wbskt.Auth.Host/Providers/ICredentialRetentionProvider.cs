namespace Wbskt.Auth.Host.Providers;

public interface ICredentialRetentionProvider
{
    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> rows per credential table that expired before
    /// <paramref name="cutoffUtc"/>; returns the total deleted.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Up to <paramref name="batchSize"/> invitations that ran out unused by <paramref name="nowUtc"/>
    /// and were not returned before; each is returned once.
    /// </summary>
    Task<IReadOnlyCollection<ExpiredInvitation>> AnnounceExpiredInvitationsAsync(DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default);
}

public sealed record ExpiredInvitation(Guid RefId, int TenantId, string Email, DateTime ExpiresAt);
