using System.Net.WebSockets;

namespace Wbskt.Socket.Host.Infrastructure;

/// <summary>The connections this socket host holds, at most one per client.</summary>
public interface IConnectionManager
{
    /// <summary>
    /// Makes <paramref name="connection"/> the client's connection on this host. The newest
    /// connection wins, as it does across hosts: one already held is asked to close as superseded.
    /// </summary>
    void Register(Guid clientRefId, ClientConnection connection);

    /// <summary>Forgets <paramref name="connection"/>, unless the client's connection has already been replaced.</summary>
    bool Unregister(Guid clientRefId, ClientConnection connection);

    /// <summary>Asks the client's connection on this host, if it has one, to close; false when there is none.</summary>
    Task<bool> RequestCloseAsync(Guid clientRefId, DisconnectReason reason, WebSocketCloseStatus status, string description);

    ClientConnection? GetConnection(Guid clientRefId);

    /// <summary>A snapshot of every connection held.</summary>
    IReadOnlyCollection<KeyValuePair<Guid, ClientConnection>> GetConnections();
}
