using System.Collections.Concurrent;

namespace Wbskt.Gateway.Core.Infrastructure;

internal sealed class ConnectionManager : IConnectionManager
{
    internal const string SupersededDescription = "Superseded by a newer connection.";

    private readonly ILogger<ConnectionManager> _logger;
    private readonly ConcurrentDictionary<Guid, IDeviceConnection> _connections = new();

    public ConnectionManager(ILogger<ConnectionManager> logger)
    {
        _logger = logger;
    }

    public void Register(Guid clientRefId, IDeviceConnection connection)
    {
        IDeviceConnection? replaced = null;
        _connections.AddOrUpdate(clientRefId, connection, (_, existing) =>
        {
            replaced = existing;
            return connection;
        });

        if (replaced != null && !ReferenceEquals(replaced, connection))
        {
            // A device that reconnects before this host noticed its old socket die (Wi-Fi drop, a
            // half-open TCP connection) is let in at once; the old connection is the stale one.
            _logger.LogInformation("Client {ClientRefId} connected again; closing its previous connection.", clientRefId);
            _ = replaced.RequestCloseAsync(DisconnectReason.Superseded, SupersededDescription);
        }
    }

    public bool Unregister(Guid clientRefId, IDeviceConnection connection)
    {
        return _connections.TryRemove(KeyValuePair.Create(clientRefId, connection));
    }

    public async Task<bool> RequestCloseAsync(Guid clientRefId, DisconnectReason reason, string description)
    {
        if (!_connections.TryGetValue(clientRefId, out var connection))
        {
            return false;
        }

        await connection.RequestCloseAsync(reason, description);
        return true;
    }

    public IDeviceConnection? GetConnection(Guid clientRefId)
    {
        _connections.TryGetValue(clientRefId, out var connection);
        return connection;
    }

    public IReadOnlyCollection<KeyValuePair<Guid, IDeviceConnection>> GetConnections()
    {
        return _connections.ToArray();
    }
}
