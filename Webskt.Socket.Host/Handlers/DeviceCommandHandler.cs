using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MassTransit;
using Webskt.EventBus.Abstractions;
using Webskt.Events.Shared;
using Webskt.Socket.Host.Infrastructure;

namespace Webskt.Socket.Host.Handlers;

public sealed class DeviceCommandHandler : IConsumer<DeviceCommandEvent>
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

    public async Task Consume(ConsumeContext<DeviceCommandEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        var socket = _connectionManager.GetConnection(@event.TargetClientRefId);

        if (socket == null)
        {
            _logger.LogDebug("Received command for {ClientRefId} but it is not connected to this node.", @event.TargetClientRefId);
            return;
        }

        if (socket.State != WebSocketState.Open)
        {
            await _eventBus.PublishAsync(new DeviceCommandFailedEvent(@event.TargetClientRefId, @event.Action, "Socket not open"), ct);
            return;
        }

        try
        {
            _logger.LogInformation("Sending command {Action} to client {ClientRefId}.", @event.Action, @event.TargetClientRefId);

            var message = new
            {
                type = "command",
                action = @event.Action,
                payload = @event.Payload
            };

            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);

            await _eventBus.PublishAsync(new DeviceCommandDeliveredEvent(@event.TargetClientRefId, @event.Action), ct);
        }
        catch (Exception ex)
        {
            await _eventBus.PublishAsync(new DeviceCommandFailedEvent(@event.TargetClientRefId, @event.Action, ex.Message), ct);
            throw;
        }
    }
}
