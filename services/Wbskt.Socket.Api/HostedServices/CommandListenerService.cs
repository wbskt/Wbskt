using Wbskt.Common.Events;
using Wbskt.EventBus;
using Wbskt.Socket.Api.Services;

namespace Wbskt.Socket.Api.HostedServices;

public class CommandListenerService : IHostedService
{
    private readonly IEventBus _eventBus;

    public CommandListenerService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _eventBus.Subscribe<SendCommandToClientEvent, SendCommandToClientEventHandler>();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public class SendCommandToClientEventHandler : IEventHandler<SendCommandToClientEvent>
{
    private readonly IClientConnectionManager _connectionManager;

    public SendCommandToClientEventHandler(IClientConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public async Task HandleAsync(SendCommandToClientEvent @event, CancellationToken cancellationToken)
    {
        var socket = _connectionManager.GetSocketById(@event.ClientId);
        if (socket != null)
        {
            var buffer = System.Text.Encoding.UTF8.GetBytes(@event.Payload);
            await socket.SendAsync(new ArraySegment<byte>(buffer), System.Net.WebSockets.WebSocketMessageType.Text, true, cancellationToken);
        }
    }
}
