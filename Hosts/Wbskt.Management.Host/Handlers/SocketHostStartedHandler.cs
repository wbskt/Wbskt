using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Events.Hosting;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// A socket host that restarts under the same host id (a container restart keeps its hostname) lost
/// every connection it held, and none of them got a disconnect. The heartbeat lease does not catch
/// them, because the restarted host renews it at once, so its start clears exactly the presence it
/// used to hold and announces each client as disconnected; live clients reconnect within their retry
/// window and flip back through <c>ClientConnectedEvent</c>. A host on a new id (every deploy) leaves
/// nothing under its id, and the presence its predecessor left is cleared by
/// <see cref="Services.StalePresenceSweeper"/>.
/// </summary>
public sealed class SocketHostStartedHandler : IConsumer<SocketHostStartedEvent>
{
    internal const string Reason = "The socket host holding the connection restarted.";

    private readonly IClientProvider _clientProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SocketHostStartedHandler> _logger;

    public SocketHostStartedHandler(IClientProvider clientProvider, IEventBus eventBus, ILogger<SocketHostStartedHandler> logger)
    {
        _clientProvider = clientProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SocketHostStartedEvent> context)
    {
        string hostId = context.Message.HostId;
        var cleared = await _clientProvider.ResetPresenceByHostAsync(hostId, context.CancellationToken);
        if (cleared.Count == 0)
        {
            return;
        }

        _logger.LogWarning("Socket host {HostId} restarted; marked {Count} clients it held offline.", hostId, cleared.Count);
        foreach (var client in cleared)
        {
            // The database already says offline; the event tells everyone else. A failure here only
            // costs the announcement, so it is logged and the rest still go out.
            try
            {
                await _eventBus.PublishAsync(new ClientDisconnectedEvent(client.ClientRefId, client.ClientId, client.WorkspaceId, Reason, hostId), context.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Client {ClientRefId} was marked offline, but its disconnect could not be announced.", client.ClientRefId);
            }
        }
    }
}
