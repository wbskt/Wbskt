using System.Net.WebSockets;

namespace Wbskt.Socket.Api.Services;

public interface IClientConnectionManager
{
    Task OnConnected(int clientId, WebSocket socket);
    Task OnDisconnected(int clientId);
    WebSocket? GetSocketById(int clientId);
}
