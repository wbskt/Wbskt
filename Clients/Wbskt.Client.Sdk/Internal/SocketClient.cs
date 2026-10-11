using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Wbskt.Protocol;

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
        _webSocket.Options.AddSubProtocol(SocketProtocol.V1);
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

    private async Task ReceiveLoopAsync()
    {
        using var reader = new WebSocketMessageReader(_webSocket, SocketProtocol.MaxMessageBytes);
        try
        {
            while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(_cts.Token);
                switch (read.Kind)
                {
                    case FrameKind.Closed:
                        await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", _cts.Token);
                        return;
                    case FrameKind.TooBig:
                        // Oversized message; drop the connection and let the reconnect monitor recover.
                        return;
                    case FrameKind.Binary:
                        continue;
                }

                using var doc = JsonDocument.Parse(read.Text!);
                var type = doc.RootElement.GetProperty("type").GetString();

                switch (type)
                {
                    case MessageTypes.Ping:
                        var ping = doc.RootElement.GetProperty("payload").Deserialize<PingPayload>()!;
                        await SendAsync(new SocketMessage(MessageTypes.Pong, new PongPayload(ping.Timestamp, ping.Id)));
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
                                await SendAsync(new SocketMessage(MessageTypes.Ack, new AckPayload(commandId, type, AckPayload.Expired)));
                                break;
                            }

                            // Transport-level delivery ack; app-level acks are ordinary messages.
                            await SendAsync(new SocketMessage(MessageTypes.Ack, new AckPayload(commandId)));
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
