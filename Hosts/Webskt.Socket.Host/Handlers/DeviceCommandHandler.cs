using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MassTransit;
using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;
using Webskt.Socket.Host.Infrastructure;

namespace Webskt.Socket.Host.Handlers;

public sealed class DeviceCommandHandler : IConsumer<DeviceControlEvent>
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<DeviceCommandHandler> _logger;
    private readonly IEventBus _eventBus;

    public DeviceCommandHandler(IConnectionManager connectionManager, ILogger<DeviceCommandHandler> logger, IEventBus eventBus)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _eventBus = eventBus;
    }

    public async Task Consume(ConsumeContext<DeviceControlEvent> context)
    {
        var socket = _connectionManager.GetConnection(context.Message.ClientRefId);
        if (socket?.State != WebSocketState.Open)
        {
            _logger.LogDebug("Received command for {ClientRefId} but it is not connected or open.", context.Message.ClientRefId);
            return;
        }

        try
        {
            switch (context.Message)
            {
                case DevicePingEvent ping:
                    await HandlePingAsync(socket, ping, context.CancellationToken);
                    break;
            
                case DeviceCommandEvent command:
                    await HandleCommandAsync(socket, command, context.CancellationToken);
                    break;
            }

            await _eventBus.PublishAsync(new DeviceCommandDeliveredEvent(context.Message.ClientRefId, context.Message.Action, context.Message.WorkspaceId), context.CancellationToken);
        }
        catch (Exception ex)
        {
            await _eventBus.PublishAsync(new DeviceCommandFailedEvent(context.Message.ClientRefId, context.Message.Action, context.Message.WorkspaceId, ex.Message), context.CancellationToken);
        }
    }

    private async Task HandlePingAsync(WebSocket socket, DevicePingEvent ping, CancellationToken ct)
    {
        var message = new { type = "command", action = ping.Action, payload = new { timestamp = ping.PingTime } };
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task HandleCommandAsync(WebSocket socket, DeviceCommandEvent command, CancellationToken ct)
    {

        _logger.LogInformation("Sending command {Action} to client {ClientRefId}.", command.Action, command.ClientRefId);

        var message = new { type = "command", action = command.Action, payload = command.Payload };
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }
}
