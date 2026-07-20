using System.Net.WebSockets;

namespace Wbskt.Socket.Host.Infrastructure;

public interface IConnectionManager
{
    bool TryAddConnection(Guid clientRefId, ClientConnection connection);
    Task RemoveConnectionAsync(Guid clientRefId, WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure,
        string closeDescription = "Closed by manager", CancellationToken cancellationToken = default);
    ClientConnection? GetConnection(Guid clientRefId);
    IReadOnlyCollection<Guid> GetConnectedClients();
}
