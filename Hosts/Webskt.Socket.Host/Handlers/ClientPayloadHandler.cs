using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MassTransit;
using Webskt.EventBus.Abstractions;
using Webskt.Events.Client;
using Webskt.Socket.Host.Infrastructure;

namespace Webskt.Socket.Host.Handlers;

public sealed class ClientPayloadHandler : IConsumer<ClientPayloadEvent>
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<ClientPayloadHandler> _logger;
    private readonly IEventBus _eventBus;

    public ClientPayloadHandler(IConnectionManager connectionManager, ILogger<ClientPayloadHandler> logger, IEventBus eventBus)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _eventBus = eventBus;
    }

    public async Task Consume(ConsumeContext<ClientPayloadEvent> context)
    {
        var socket = _connectionManager.GetConnection(context.Message.ClientRefId);
        if (socket?.State != WebSocketState.Open)
        {
            _logger.LogDebug("Received command for {ClientRefId} but it is not connected or open.", context.Message.ClientRefId);
            return;
        }

        try
        {
            await HandleCommandAsync(socket, context.Message, context.CancellationToken);
            await _eventBus.PublishAsync(new ClientPayloadDeliveredEvent(context.Message.ClientRefId, context.Message.WorkspaceId, context.Message.MessageType), context.CancellationToken);
        }
        catch (Exception ex)
        {
            await _eventBus.PublishAsync(new ClientPayloadFailedEvent(context.Message.ClientRefId, context.Message.WorkspaceId, context.Message.MessageType, ex.Message), context.CancellationToken);
        }
    }

    private async Task HandleCommandAsync(WebSocket socket, ClientPayloadEvent command, CancellationToken ct)
    {
        _logger.LogInformation("Sending command {Action} to client {ClientRefId}.", command.MessageType, command.ClientRefId);

        var message = new { type = "command", action = command.MessageType, payload = command.Payload };
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }
}
