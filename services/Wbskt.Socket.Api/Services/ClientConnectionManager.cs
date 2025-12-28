using System.Collections.Concurrent;
using System.Net.WebSockets;
using Wbskt.Common.Events;
using Wbskt.EventBus;

namespace Wbskt.Socket.Api.Services;

public class ClientConnectionManager : IClientConnectionManager
{
    private readonly ConcurrentDictionary<int, WebSocket> _sockets = new();
    private readonly ILogger<ClientConnectionManager> _logger;
    private readonly IEventBus _eventBus;

    public ClientConnectionManager(ILogger<ClientConnectionManager> logger, IEventBus eventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task OnConnected(int clientId, WebSocket socket)
    {
        _sockets.TryAdd(clientId, socket);
        _logger.LogInformation("Client {ClientId} connected.", clientId);

        // Start the message loop
        await Receive(socket, async (result, buffer) =>
        {
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var payload = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                _logger.LogInformation("Message received from {ClientId}: {Payload}", clientId, payload);

                var clientEvent = new ClientDataReceivedEvent(clientId, Guid.Empty, payload); // todo: We need to get the ClientUniqueId from the claims
                await _eventBus.PublishAsync(clientEvent, CancellationToken.None);
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                await OnDisconnected(clientId);
            }
        });
    }

    public async Task OnDisconnected(int clientId)
    {
        if (_sockets.TryRemove(clientId, out var socket))
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnected", CancellationToken.None);
            _logger.LogInformation("Client {ClientId} disconnected.", clientId);
        }
    }

    public WebSocket? GetSocketById(int clientId)
    {
        return _sockets.GetValueOrDefault(clientId);
    }

    private async Task Receive(WebSocket socket, Action<WebSocketReceiveResult, byte[]> handleMessage)
    {
        var buffer = new byte[1024 * 4];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            handleMessage(result, buffer);
        }
    }
}
