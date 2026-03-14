using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Wbskt.Socket.Host.Infrastructure;

internal sealed class ConnectionManager : IConnectionManager
{
    private readonly ILogger<ConnectionManager> _logger;
    private readonly ConcurrentDictionary<Guid, WebSocket> _connections = new();

    public ConnectionManager(ILogger<ConnectionManager> logger)
    {
        _logger = logger;
    }

    public bool TryAddConnection(Guid clientRefId, WebSocket socket)
    {
        if (!_connections.TryAdd(clientRefId, socket))
        {
            _logger.LogWarning("Connection attempt rejected. Client {ClientRefId} is already connected.", clientRefId);
            return false;
        }
        return true;
    }

    public async Task RemoveConnectionAsync(Guid clientRefId, CancellationToken cancellationToken = default)
    {
        if (_connections.TryRemove(clientRefId, out var socket))
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by manager", cancellationToken);
                }
                catch
                {
                    // Ignore exceptions during close, we just want to ensure it's removed and disposed
                }
            }
            socket.Dispose();
        }
    }

    public WebSocket? GetConnection(Guid clientRefId)
    {
        _connections.TryGetValue(clientRefId, out var socket);
        return socket;
    }

    public IReadOnlyCollection<Guid> GetConnectedClients()
    {
        return _connections.Keys.ToList().AsReadOnly();
    }
}
