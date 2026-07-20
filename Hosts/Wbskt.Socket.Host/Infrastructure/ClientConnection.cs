using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Wbskt.Client.Sdk.Models;

namespace Wbskt.Socket.Host.Infrastructure;

public sealed class ClientConnection : IDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public required WebSocket Socket { get; init; }
    public required int ClientId { get; init; }
    public required int WorkspaceId { get; init; }

    // Timestamp of the last sys.ping sent on this connection; RTT fallback when the pong payload is unreadable.
    public DateTime? LastPingSentAt { get; set; }

    public async Task SendAsync(SocketMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        // WebSocket allows only one outstanding send; commands, pings and samplers can race here.
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose() => _sendLock.Dispose();
}
