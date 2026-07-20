using System.Net.WebSockets;
using MassTransit;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.Handlers;

// With per-instance fan-out (WP2.1), every socket-host instance observes every
// ClientConnectedEvent, including ones published by other instances. If another instance just
// accepted a connection for a client we're still holding locally with an older connection, ours
// is the stale duplicate (e.g. the client's previous host never noticed the network drop) -
// close it. The resulting ClientDisconnectedEvent's host-scoped presence clear is a no-op
// because the new host already owns ConnectedHostId (see Client_UpdatePresence.sql).
public sealed class ConnectionSupersededHandler : IConsumer<ClientConnectedEvent>
{
    private readonly IConnectionManager _connectionManager;
    private readonly BusInstanceId _busInstanceId;
    private readonly ILogger<ConnectionSupersededHandler> _logger;

    public ConnectionSupersededHandler(
        IConnectionManager connectionManager,
        BusInstanceId busInstanceId,
        ILogger<ConnectionSupersededHandler> logger)
    {
        _connectionManager = connectionManager;
        _busInstanceId = busInstanceId;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientConnectedEvent> context)
    {
        var message = context.Message;
        if (message.HostId == _busInstanceId.Value)
        {
            // Our own connect event, echoed back to us; nothing to supersede.
            return;
        }

        var existing = _connectionManager.GetConnection(message.ClientRefId);
        if (existing == null || existing.Socket.State != WebSocketState.Open)
        {
            return;
        }

        if (existing.EstablishedAtUtc >= message.CreatedAtUtc)
        {
            return;
        }

        _logger.LogInformation(
            "Client {ClientRefId} connected on host {HostId}; closing our superseded connection.",
            message.ClientRefId, message.HostId);

        await _connectionManager.RemoveConnectionAsync(
            message.ClientRefId,
            WebSocketCloseStatus.PolicyViolation,
            "Superseded by a newer connection on another host.",
            context.CancellationToken);
    }
}
