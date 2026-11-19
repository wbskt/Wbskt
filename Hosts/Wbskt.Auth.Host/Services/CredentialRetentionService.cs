using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Hourly sweep of spent credentials: refresh, reset and verification tokens and unaccepted
/// invitations, deleted <see cref="Grace"/> after they expired. Nothing deleted them before, so the
/// tables grew with every sign-in for the life of the deployment. Each sweep first logs the invitations
/// that ran out unused since the last one, in the tenant's workspaces' audit logs, and then deletes the
/// accounts whose email was never confirmed <see cref="UnconfirmedAccountLifetime"/> after they registered.
/// </summary>
public sealed class CredentialRetentionService : PeriodicWorker
{
    internal const int BatchSize = 5000;

    /// <summary>
    /// How long an expired credential is kept. It cannot be used, but a presented token that is
    /// recognised as rotated-away or revoked is evidence of theft, which an unknown one is not.
    /// </summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromDays(30);

    /// <summary>
    /// How long an account may wait for its owner to confirm the address. Until then it holds the
    /// username and the address, so someone who mistyped theirs, or registered someone else's, keeps
    /// the real owner out of both.
    /// </summary>
    internal static readonly TimeSpan UnconfirmedAccountLifetime = TimeSpan.FromDays(30);

    internal const int UnconfirmedBatchSize = 100;

    private readonly TimeProvider _timeProvider;

    public CredentialRetentionService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<CredentialRetentionService> logger)
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger, timeProvider)
    {
        _timeProvider = timeProvider;
    }

    protected override string Name => "Credential retention";

    // A failed sweep is retried on the next tick; it only lets the tables grow for an hour longer.
    protected override TimeSpan Interval { get; } = TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await SweepAsync(services, ct);
    }

    /// <summary>One sweep in a scope of its own; see <see cref="SweepAsync(IServiceProvider, CancellationToken)"/>.</summary>
    internal async Task<long> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        return await SweepAsync(scope.ServiceProvider, cancellationToken);
    }

    /// <summary>One sweep: repeats until a pass deletes nothing. Returns the total deleted.</summary>
    private async Task<long> SweepAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var provider = services.GetRequiredService<ICredentialRetentionProvider>();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoffUtc = nowUtc - Grace;

        // Before the deletes, so an invitation that expired longer ago than the grace period (one from
        // before this sweep logged expiries) is logged before it is gone.
        await AnnounceExpiredInvitationsAsync(provider, nowUtc, cancellationToken);

        long total = 0;
        int deleted;
        do
        {
            deleted = await provider.DeleteExpiredAsync(cutoffUtc, BatchSize, cancellationToken);
            total += deleted;
        }
        while (deleted > 0);

        if (total > 0)
        {
            Logger.LogInformation("Credential retention deleted {Count} rows that expired before {CutoffUtc:o}.", total, cutoffUtc);
        }

        await DeleteUnconfirmedAccountsAsync(services, provider, nowUtc, cancellationToken);

        return total;
    }

    /// <summary>
    /// Deletes, one at a time, every account never confirmed within <see cref="UnconfirmedAccountLifetime"/>.
    /// One that was confirmed or used meanwhile is skipped. Returns how many were deleted.
    /// </summary>
    private async Task<int> DeleteUnconfirmedAccountsAsync(IServiceProvider services, ICredentialRetentionProvider provider, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var deletion = services.GetRequiredService<IAccountDeletionService>();
        var createdBeforeUtc = nowUtc - UnconfirmedAccountLifetime;
        var afterId = 0;
        var deleted = 0;

        IReadOnlyCollection<int> batch;
        do
        {
            batch = await provider.GetUnconfirmedAccountsAsync(createdBeforeUtc, nowUtc, afterId, UnconfirmedBatchSize, cancellationToken);
            foreach (var userId in batch)
            {
                afterId = Math.Max(afterId, userId);
                try
                {
                    if (await deletion.DeleteUnconfirmedAsync(userId, createdBeforeUtc, cancellationToken))
                    {
                        deleted++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One account that cannot be deleted must not keep every later one: it is tried again next tick.
                    Logger.LogError(ex, "Could not delete unconfirmed account {UserId}.", userId);
                }
            }
        }
        while (batch.Count == UnconfirmedBatchSize);

        if (deleted > 0)
        {
            Logger.LogInformation("Credential retention deleted {Count} accounts never confirmed since before {CreatedBeforeUtc:o}.", deleted, createdBeforeUtc);
        }

        return deleted;
    }

    private async Task AnnounceExpiredInvitationsAsync(ICredentialRetentionProvider provider, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // Each row comes back once, and its event is already in the outbox: the procedure marks the row
        // and queues the event in one transaction, so a broker outage cannot lose the audit entry.
        int announced = 0;
        IReadOnlyCollection<ExpiredInvitation> expired;
        do
        {
            expired = await provider.AnnounceExpiredInvitationsAsync(nowUtc, BatchSize, cancellationToken);
            announced += expired.Count;
        }
        while (expired.Count == BatchSize);

        if (announced > 0)
        {
            Logger.LogInformation("Credential retention queued {Count} expired invitations for the audit log.", announced);
        }
    }
}
