using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
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
public sealed class StalePresenceSweeper : BackgroundService
{
    internal const string Reason = "The socket host holding the connection stopped responding.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StalePresenceSweeper> _logger;
    private readonly TimeSpan _staleAfter;
    private readonly TimeSpan _interval;

    public StalePresenceSweeper(IServiceScopeFactory scopeFactory, IOptions<PresenceOptions> options, ILogger<StalePresenceSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _staleAfter = TimeSpan.FromSeconds(Math.Max(30, options.Value.HostStaleAfterSeconds));
        _interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.SweepIntervalSeconds));
    }

    /// <summary>One sweep. Returns how many clients were marked disconnected.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IClientProvider>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

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
                _logger.LogWarning(ex, "Client {ClientRefId} was marked offline, but its disconnect could not be announced.", client.ClientRefId);
            }
        }

        if (expired.Count > 0)
        {
            _logger.LogWarning("Marked {Count} clients offline: their socket hosts ({HostIds}) stopped sending heartbeats.",
                expired.Count, string.Join(", ", expired.Select(c => c.HostId ?? "none").Distinct()));
        }

        return expired.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "The stale presence sweep failed; it runs again in {Interval}.", _interval);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
