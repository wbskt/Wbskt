using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Events.Hosting;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// Repairs presence a live socket host lost: a client the database has on that host which connected
/// before the host's snapshot but isn't in it is cleared and announced as disconnected, so offline
/// triggers fire and sends queue instead of going to a host that no longer has the client.
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
        var cleared = await _clients.ReconcilePresenceByHostAsync(snapshot.HostId, snapshot.ClientRefIds, snapshot.TakenAtUtc, Margin, context.CancellationToken);
        foreach (var client in cleared)
        {
            try
            {
                // Dated by the snapshot, on the host's clock, so a reconnect the host saw after it still wins.
                var disconnected = new ClientDisconnectedEvent(client.ClientRefId, client.ClientId, client.WorkspaceId, Reason, snapshot.HostId) { CreatedAtUtc = snapshot.TakenAtUtc };
                await _eventBus.PublishAsync(disconnected, context.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Client {ClientRefId} was marked offline, but its disconnect could not be announced.", client.ClientRefId);
            }
        }

        if (cleared.Count > 0)
        {
            _logger.LogWarning("Marked {Count} clients offline: socket host {HostId} no longer held them, and their disconnects were lost.", cleared.Count, snapshot.HostId);
        }
    }
}
