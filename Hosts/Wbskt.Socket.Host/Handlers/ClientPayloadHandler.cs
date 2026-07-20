using System.Net.WebSockets;
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
        var connection = _connectionManager.GetConnection(context.Message.ClientRefId);
        if (connection?.Socket.State != WebSocketState.Open)
        {
            _logger.LogDebug("Received command for {ClientRefId} but it is not connected or open.", context.Message.ClientRefId);
            return;
        }

        try
        {
            await HandleCommandAsync(connection, context.Message, context.CancellationToken);
            await _eventBus.PublishAsync(new ClientCommandDeliveredEvent(context.Message.ClientRefId, context.Message.ClientId, context.Message.WorkspaceId, context.Message.Type, context.Message.Payload, context.Message.CommandId), context.CancellationToken);
        }
        catch (Exception ex)
        {
            await _eventBus.PublishAsync(new ClientCommandFailedEvent(context.Message.ClientRefId, context.Message.ClientId, context.Message.WorkspaceId, context.Message.Type, ex.Message), context.CancellationToken);
        }
    }

    private async Task HandleCommandAsync(ClientConnection connection, ClientCommandEvent command, CancellationToken ct)
    {
        _logger.LogInformation("Sending command {Type} to client {ClientRefId}.", command.Type, command.ClientRefId);

        // The SDK auto-acks frames carrying a commandId with sys.ack.
        var message = new SocketMessage(command.Type, command.Payload, command.CommandId?.ToString());
        await connection.SendAsync(message, ct);
    }
}
