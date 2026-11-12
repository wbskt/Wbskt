using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Events.Hosting;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// Repairs presence from a live socket host's snapshot, both ways. A client the database has on that host
/// which connected before the snapshot but isn't in it is cleared and announced as disconnected, so
/// offline triggers fire and sends queue instead of going to a host that no longer has the client. A client
/// in it that the database shows offline (the stale-presence sweep cleared it while the host's heartbeats
/// could not reach the broker) is set back online and announced as connected, which also delivers the
/// commands that queued meanwhile.
/// </summary>
public sealed class SocketHostPresenceSnapshotHandler : IConsumer<SocketHostPresenceSnapshot>
{
    internal const string Reason = "The socket host holding the connection no longer had it.";

    // A connect is recorded with the host's time before the connection is registered there, so one that
    // connected just before the snapshot can be missing from it.
    internal static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    private readonly IClientProvider _clients;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SocketHostPresenceSnapshotHandler> _logger;

    public SocketHostPresenceSnapshotHandler(IClientProvider clients, IEventBus eventBus, ILogger<SocketHostPresenceSnapshotHandler> logger)
    {
        _clients = clients;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SocketHostPresenceSnapshot> context)
    {
        var snapshot = context.Message;
        var repaired = await _clients.ReconcilePresenceByHostAsync(snapshot.HostId, snapshot.ClientRefIds, snapshot.TakenAtUtc, Margin, context.CancellationToken);
        foreach (var client in repaired)
        {
            try
            {
                // Dated by the snapshot, on the host's clock, so a change the host saw after it still wins.
                // Published by its own type: the bus routes on the static type.
                if (client.Connected)
                {
                    var connected = new ClientConnectedEvent(client.ClientRefId, client.ClientId, client.WorkspaceId, snapshot.HostId) { CreatedAtUtc = snapshot.TakenAtUtc };
                    await _eventBus.PublishAsync(connected, context.CancellationToken);
                }
                else
                {
                    var disconnected = new ClientDisconnectedEvent(client.ClientRefId, client.ClientId, client.WorkspaceId, Reason, snapshot.HostId) { CreatedAtUtc = snapshot.TakenAtUtc };
                    await _eventBus.PublishAsync(disconnected, context.CancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Client {ClientRefId}'s presence was repaired, but the change could not be announced.", client.ClientRefId);
            }
        }

        int cleared = repaired.Count(c => !c.Connected);
        if (cleared > 0)
        {
            _logger.LogWarning("Marked {Count} clients offline: socket host {HostId} no longer held them, and their disconnects were lost.", cleared, snapshot.HostId);
        }

        int restored = repaired.Count - cleared;
        if (restored > 0)
        {
            _logger.LogWarning("Marked {Count} clients online again: socket host {HostId} still held them after their presence expired.", restored, snapshot.HostId);
        }
    }
}
