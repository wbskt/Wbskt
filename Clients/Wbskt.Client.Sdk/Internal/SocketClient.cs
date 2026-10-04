using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Wbskt.Client.Sdk.Models;

namespace Wbskt.Client.Sdk.Internal;

internal sealed class SocketClient : IAsyncDisposable
{
    private ClientWebSocket _webSocket = new();
    private CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly string _baseUrl;

    public event Action<string, object?, string?>? OnMessageReceived;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public bool IsConnected => _webSocket.State == WebSocketState.Open;

    public SocketClient(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    public async Task ConnectAsync(string token)
    {
        if (_webSocket.State == WebSocketState.Open)
        {
            return;
        }

        // Reset state for new connection
        if (_cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }

        _webSocket.Dispose();
        _webSocket = new ClientWebSocket();

        var uri = new Uri($"{_baseUrl.TrimEnd('/')}/ws");
        _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        await _webSocket.ConnectAsync(uri, _cts.Token);
        OnConnected?.Invoke();
        
        _ = Task.Run(ReceiveLoopAsync, _cts.Token);
    }

    public async Task SendAsync(SocketMessage message)
    {
        if (_webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Socket not connected.");
        }

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        // ClientWebSocket allows one send at a time; pongs, acks and app messages share the socket.
        await _sendLock.WaitAsync();
        try
        {
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }
        catch (Exception)
        {
            await AbortAsync();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private const int MaxMessageBytes = 64 * 1024;

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[1024 * 4];
        using var messageBuffer = new MemoryStream();
        try
        {
            while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                // A logical message can span multiple frames; assemble until EndOfMessage.
                messageBuffer.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", _cts.Token);
                        return;
                    }

                    if (messageBuffer.Length + result.Count > MaxMessageBytes)
                    {
                        // Oversized message; drop the connection and let the reconnect monitor recover.
                        return;
                    }

                    messageBuffer.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                using var doc = JsonDocument.Parse(json);
                var type = doc.RootElement.GetProperty("type").GetString();

                switch (type)
                {
                    case "sys.ping":
                        var ts = doc.RootElement.GetProperty("payload").GetProperty("timestamp").GetDateTime();
                        await SendAsync(new SocketMessage("sys.pong", new { originalTimestamp = ts }));
                        break;
                    default:
                        var payload = doc.RootElement.TryGetProperty("payload", out var p) ? (object)p.Clone() : null;
                        var commandId = doc.RootElement.TryGetProperty("commandId", out var c) && c.ValueKind == JsonValueKind.String
                            ? c.GetString()
                            : null;

                        if (commandId != null)
                        {
                            if (CommandExpiry.IsExpired(doc.RootElement, DateTimeOffset.UtcNow))
                            {
                                // Answered, so the platform reports it as refused rather than lost.
                                await SendAsync(new SocketMessage("sys.ack", new { commandId, type, refused = "expired" }));
                                break;
                            }

                            // Transport-level delivery ack; app-level acks are ordinary messages.
                            await SendAsync(new SocketMessage("sys.ack", new { commandId }));
                        }

                        OnMessageReceived?.Invoke(type ?? "unknown", payload, commandId);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // ignored
        }
        finally
        {
            await AbortAsync();
            OnDisconnected?.Invoke();
        }
    }

    private Task AbortAsync()
    {
        if (_webSocket.State is WebSocketState.Closed or WebSocketState.Aborted)
        {
            return Task.CompletedTask;
        }

        try { _webSocket.Abort(); }
        catch
        {
            // ignored
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        await AbortAsync();
        _webSocket.Dispose();
        _cts.Dispose();
        _sendLock.Dispose();
    }
}
