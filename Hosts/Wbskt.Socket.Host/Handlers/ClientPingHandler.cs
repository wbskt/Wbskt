using System.Net.WebSockets;
using MassTransit;
using Wbskt.Client.Sdk.Models;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.Handlers;

public sealed class ClientPingHandler : IConsumer<ClientPingEvent>
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<ClientPingHandler> _logger;

    public ClientPingHandler(IConnectionManager connectionManager, ILogger<ClientPingHandler> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientPingEvent> context)
    {
        var connection = _connectionManager.GetConnection(context.Message.ClientRefId);
        if (connection?.Socket.State != WebSocketState.Open)
        {
            _logger.LogDebug("Received ping for {ClientRefId} but it is not connected or open.", context.Message.ClientRefId);
            return;
        }

        // The timestamp is stamped at send (not the event's PingTime) so bus latency stays out of the RTT.
        var sentAt = DateTime.UtcNow;
        connection.LastPingSentAt = sentAt;

        await connection.SendAsync(new SocketMessage("sys.ping", new { timestamp = sentAt }), context.CancellationToken);
        _logger.LogDebug("Sent sys.ping to client {ClientRefId}.", context.Message.ClientRefId);
    }
}
