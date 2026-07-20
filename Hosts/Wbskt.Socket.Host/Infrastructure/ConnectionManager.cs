using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Wbskt.Socket.Host.Infrastructure;

internal sealed class ConnectionManager : IConnectionManager
{
    private readonly ILogger<ConnectionManager> _logger;
    private readonly ConcurrentDictionary<Guid, ClientConnection> _connections = new();

    public ConnectionManager(ILogger<ConnectionManager> logger)
    {
        _logger = logger;
    }

    public bool TryAddConnection(Guid clientRefId, ClientConnection connection)
    {
        if (!_connections.TryAdd(clientRefId, connection))
        {
            _logger.LogWarning("Connection attempt rejected. Client {ClientRefId} is already connected.", clientRefId);
            return false;
        }
        return true;
    }

    public async Task RemoveConnectionAsync(Guid clientRefId, WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure,
        string closeDescription = "Closed by manager", CancellationToken cancellationToken = default)
    {
        if (_connections.TryRemove(clientRefId, out var connection))
        {
            var socket = connection.Socket;
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(closeStatus, closeDescription, cancellationToken);
                }
                catch
                {
                    // Ignore exceptions during close, we just want to ensure it's removed and disposed
                }
            }
            socket.Dispose();
            connection.Dispose();
        }
    }

    public ClientConnection? GetConnection(Guid clientRefId)
    {
        _connections.TryGetValue(clientRefId, out var connection);
        return connection;
    }

    public IReadOnlyCollection<Guid> GetConnectedClients()
    {
        return _connections.Keys.ToList().AsReadOnly();
    }
}
