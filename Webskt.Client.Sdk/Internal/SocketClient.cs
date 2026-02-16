using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Webskt.Client.Sdk.Internal;

internal sealed class SocketClient : IAsyncDisposable
{
    private ClientWebSocket _webSocket = new();
    private CancellationTokenSource _cts = new();
    private readonly string _baseUrl;

    public event Action<string, object?>? OnMessageReceived;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public bool IsConnected => _webSocket.State == WebSocketState.Open;

    public SocketClient(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    public async Task ConnectAsync(string token)
    {
        if (_webSocket.State == WebSocketState.Open) return;

        // Reset state for new connection
        if (_cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }

        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();

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
        
        try
        {
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }
        catch (Exception)
        {
            await AbortAsync();
            throw;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[1024 * 4];
        try
        {
            while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", _cts.Token);
                    break;
                }

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
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            await AbortAsync();
            OnDisconnected?.Invoke();
        }
    }

    private async Task AbortAsync()
    {
        if (_webSocket.State != WebSocketState.Closed && _webSocket.State != WebSocketState.Aborted)
        {
            try { _webSocket.Abort(); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await AbortAsync();
        _webSocket.Dispose();
        _cts.Dispose();
    }
}
