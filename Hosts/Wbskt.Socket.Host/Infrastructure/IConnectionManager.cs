using System.Net.WebSockets;

namespace Wbskt.Socket.Host.Infrastructure;

public interface IConnectionManager
{
    bool TryAddConnection(Guid clientRefId, WebSocket socket);
    Task RemoveConnectionAsync(Guid clientRefId, CancellationToken cancellationToken = default);
    WebSocket? GetConnection(Guid clientRefId);
    IReadOnlyCollection<Guid> GetConnectedClients();
}