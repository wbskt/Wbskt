using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MassTransit;
using Wbskt.Client.Sdk.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.Handlers;

public sealed class ClientPayloadHandler : IConsumer<ClientCommandEvent>
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

    public async Task Consume(ConsumeContext<ClientCommandEvent> context)
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
            await _eventBus.PublishAsync(new ClientCommandDeliveredEvent(context.Message.ClientRefId, context.Message.ClientId, context.Message.WorkspaceId, context.Message.Type, context.Message.Payload), context.CancellationToken);
        }
        catch (Exception ex)
        {
            await _eventBus.PublishAsync(new ClientCommandFailedEvent(context.Message.ClientRefId, context.Message.ClientId, context.Message.WorkspaceId, context.Message.Type, ex.Message), context.CancellationToken);
        }
    }

    private async Task HandleCommandAsync(WebSocket socket, ClientCommandEvent command, CancellationToken ct)
    {
        _logger.LogInformation("Sending command {Type} to client {ClientRefId}.", command.Type, command.ClientRefId);

        // [RJ]: TODO: command id will be used to do ack from the client
        var message = new SocketMessage(command.Type, command.Payload /*, command.CommandId */);
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }
}
