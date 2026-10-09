using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Hourly sweep of spent credentials: refresh, reset and verification tokens and unaccepted
/// invitations, deleted <see cref="Grace"/> after they expired. Nothing deleted them before, so the
/// tables grew with every sign-in for the life of the deployment. Each sweep first logs the invitations
/// that ran out unused since the last one, in the tenant's workspaces' audit logs.
/// </summary>
public sealed class CredentialRetentionService : BackgroundService
{
    internal const int BatchSize = 5000;

    /// <summary>
    /// How long an expired credential is kept. It cannot be used, but a presented token that is
    /// recognised as rotated-away or revoked is evidence of theft, which an unknown one is not.
    /// </summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromDays(30);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CredentialRetentionService> _logger;

    public CredentialRetentionService(IServiceScopeFactory scopeFactory, ILogger<CredentialRetentionService> logger)
        : this(scopeFactory, TimeProvider.System, logger)
    {
    }

    internal CredentialRetentionService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<CredentialRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>One sweep: repeats until a pass deletes nothing. Returns the total deleted.</summary>
    internal async Task<long> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICredentialRetentionProvider>();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoffUtc = nowUtc - Grace;

        // Before the deletes, so an invitation that expired longer ago than the grace period (one from
        // before this sweep logged expiries) is logged before it is gone.
        await AnnounceExpiredInvitationsAsync(scope.ServiceProvider, provider, nowUtc, cancellationToken);

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
            _logger.LogInformation("Credential retention deleted {Count} rows that expired before {CutoffUtc:o}.", total, cutoffUtc);
        }

        return total;
    }

    private static async Task AnnounceExpiredInvitationsAsync(IServiceProvider services, ICredentialRetentionProvider provider, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var eventBus = services.GetRequiredService<IEventBus>();
        var audit = services.GetService<IAuditWorkspaces>() ?? AuditWorkspaces.None;

        IReadOnlyCollection<ExpiredInvitation> expired;
        do
        {
            // Each row comes back once: the procedure marks it as announced as it returns it.
            expired = await provider.AnnounceExpiredInvitationsAsync(nowUtc, BatchSize, cancellationToken);
            var workspacesByTenant = new Dictionary<int, IReadOnlyList<int>>();
            foreach (var invitation in expired)
            {
                if (!workspacesByTenant.TryGetValue(invitation.TenantId, out var workspaceIds))
                {
                    workspacesByTenant[invitation.TenantId] = workspaceIds = await audit.OfTenantAsync(invitation.TenantId, cancellationToken);
                }

                await eventBus.PublishAsync(
                    new InvitationExpiredEvent(invitation.RefId, invitation.Email) { WorkspaceIds = workspaceIds, CreatedAtUtc = invitation.ExpiresAt },
                    cancellationToken);
            }
        }
        while (expired.Count == BatchSize);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepGuardedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepGuardedAsync(stoppingToken);
        }
    }

    private async Task SweepGuardedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The next tick retries; a failed sweep only lets the tables grow for an hour longer.
            _logger.LogError(ex, "Credential retention sweep failed.");
        }
    }
}
