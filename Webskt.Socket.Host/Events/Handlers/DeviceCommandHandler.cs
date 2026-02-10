using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Webskt.Common.Abstraction.Events;
using Webskt.Common.Abstraction.Events.Shared;
using Webskt.Socket.Host.Infrastructure;

namespace Webskt.Socket.Host.Events.Handlers;

public sealed class DeviceCommandHandler : IEventHandler<DeviceCommandEvent>
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<DeviceCommandHandler> _logger;

    public DeviceCommandHandler(IConnectionManager connectionManager, ILogger<DeviceCommandHandler> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task HandleAsync(DeviceCommandEvent @event, CancellationToken ct)
    {
        var socket = _connectionManager.GetConnection(@event.TargetClientRefId);

        if (socket == null || socket.State != WebSocketState.Open)
        {
            _logger.LogDebug("Received command for {ClientRefId} but it is not connected to this node.", @event.TargetClientRefId);
            return;
        }

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
    }
}
