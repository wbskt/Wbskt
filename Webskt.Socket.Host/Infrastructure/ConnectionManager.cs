using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Webskt.Socket.Host.Infrastructure;

public interface IConnectionManager
{
    void AddConnection(Guid clientRefId, WebSocket socket);
    Task RemoveConnectionAsync(Guid clientRefId);
    WebSocket? GetConnection(Guid clientRefId);
    IReadOnlyCollection<Guid> GetConnectedClients();
}

public sealed class ConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _connections = new();

    public void AddConnection(Guid clientRefId, WebSocket socket)
    {
        _connections.TryAdd(clientRefId, socket);
    }

    public async Task RemoveConnectionAsync(Guid clientRefId)
    {
        if (_connections.TryRemove(clientRefId, out var socket))
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by manager", CancellationToken.None);
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
