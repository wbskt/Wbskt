using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Presence as a lease: a client is shown online only while the socket host holding it keeps sending
/// heartbeats. On an interval, clears the presence of clients held by a host that has gone silent and
/// announces each as disconnected, so the console updates and offline triggers fire.
/// </summary>
/// <remarks>
/// A host's own start (SocketHostStartedEvent) only clears what a host restarting under the same id
/// left behind. A deploy starts a new container, which is a new host id, and a replica that is scaled
/// away never starts again; without this, a device that was really offline would stay "online" and
/// its offline trigger would never fire.
/// </remarks>
public sealed class StalePresenceSweeper : PeriodicWorker
{
    internal const string Reason = "The socket host holding the connection stopped responding.";

    private readonly TimeSpan _staleAfter;

    public StalePresenceSweeper(IServiceScopeFactory scopeFactory, IOptions<PresenceOptions> options, ILogger<StalePresenceSweeper> logger)
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
    {
        _staleAfter = TimeSpan.FromSeconds(Math.Max(30, options.Value.HostStaleAfterSeconds));
        Interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.SweepIntervalSeconds));
    }

    protected override string Name => "Stale presence sweep";

    protected override TimeSpan Interval { get; }

    // The first sweep waits one interval: after a management restart the socket hosts' heartbeats have
    // not been recorded yet, and a sweep at once would read every host as silent.
    protected override bool RunOnStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await SweepAsync(services, ct);
    }

    /// <summary>One sweep in a scope of its own. Returns how many clients were marked disconnected.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        return await SweepAsync(scope.ServiceProvider, cancellationToken);
    }

    private async Task<int> SweepAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var provider = services.GetRequiredService<IClientProvider>();
        var eventBus = services.GetRequiredService<IEventBus>();

        var expired = await provider.ExpirePresenceAsync(_staleAfter, cancellationToken);
        foreach (var client in expired)
        {
            // The database already says offline; the event tells everyone else. A failure here only
            // costs the announcement, so it is logged and the rest still go out.
            try
            {
                await eventBus.PublishAsync(new ClientDisconnectedEvent(client.ClientRefId, client.ClientId, client.WorkspaceId, Reason, client.HostId ?? string.Empty), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "Client {ClientRefId} was marked offline, but its disconnect could not be announced.", client.ClientRefId);
            }
        }

        if (expired.Count > 0)
        {
            Logger.LogWarning("Marked {Count} clients offline: their socket hosts ({HostIds}) stopped sending heartbeats.",
                expired.Count, string.Join(", ", expired.Select(c => c.HostId ?? "none").Distinct()));
        }

        return expired.Count;
    }
}
