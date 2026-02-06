using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Webskt.Client.Sdk.Internal;

internal sealed class SocketClient : IAsyncDisposable
{
    private readonly ClientWebSocket _webSocket = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _baseUrl;

    public event Action<string, object?>? OnMessageReceived;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public SocketClient(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    public async Task ConnectAsync(string token)
    {
        var uri = new Uri($"{_baseUrl.TrimEnd('/')}/ws?access_token={token}");
        await _webSocket.ConnectAsync(uri, _cts.Token);
        OnConnected?.Invoke();
        
        _ = Task.Run(ReceiveLoopAsync, _cts.Token);
    }

    public async Task SendAsync(object message)
    {
        if (_webSocket.State != WebSocketState.Open) throw new InvalidOperationException("Socket not connected.");

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[1024 * 4];
        try
        {
            while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var doc = JsonDocument.Parse(json);
                    var type = doc.RootElement.GetProperty("type").GetString();
                    
                    if (type == "command")
                    {
                        var action = doc.RootElement.GetProperty("action").GetString();
                        var payload = doc.RootElement.TryGetProperty("payload", out var p) ? (object)p : null;
                        OnMessageReceived?.Invoke(action ?? "unknown", payload);
                    }
                }
            }
        }
        catch { /* Handle/Log error */ }
        finally
        {
            OnDisconnected?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_webSocket.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
        _webSocket.Dispose();
        _cts.Dispose();
    }
}
