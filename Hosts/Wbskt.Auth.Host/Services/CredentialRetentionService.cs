using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Hourly sweep of spent credentials: refresh, reset and verification tokens and unaccepted
/// invitations, deleted <see cref="Grace"/> after they expired. Nothing deleted them before, so the
/// tables grew with every sign-in for the life of the deployment. Each sweep first logs the invitations
/// that ran out unused since the last one, in the tenant's workspaces' audit logs.
/// </summary>
public sealed class CredentialRetentionService : PeriodicWorker
{
    internal const int BatchSize = 5000;

    /// <summary>
    /// How long an expired credential is kept. It cannot be used, but a presented token that is
    /// recognised as rotated-away or revoked is evidence of theft, which an unknown one is not.
    /// </summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CredentialRetentionService> _logger;

    public CredentialRetentionService(IServiceScopeFactory scopeFactory, ILogger<CredentialRetentionService> logger)
        : this(scopeFactory, TimeProvider.System, logger)
    {
    }

    internal CredentialRetentionService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<CredentialRetentionService> logger)
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger, timeProvider)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
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
        await using var scope = _scopeFactory.CreateAsyncScope();
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
        await AnnounceExpiredInvitationsAsync(services, provider, nowUtc, cancellationToken);

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
}
