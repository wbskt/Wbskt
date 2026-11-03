using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Wbskt.Protocol;

namespace Wbskt.Client.Sdk.Internal;

internal sealed class SocketClient : IAsyncDisposable
{
    private ClientWebSocket _webSocket = new();
    private CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly string _baseUrl;
    private readonly Channel<InboundMessage> _inbox = Channel.CreateUnbounded<InboundMessage>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _dispatchCts = new();
    private readonly Task _dispatch;

    public event Action<string, object?, string?>? OnMessageReceived;

    /// <summary>The app's message handler threw. The connection stays up and the next message is handed over as usual.</summary>
    public event Action<Exception>? OnHandlerError;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public bool IsConnected => _webSocket.State == WebSocketState.Open;

    /// <summary>Why the gateway ended the last connection; <see cref="ServerClose.Other"/> when it dropped or closed otherwise.</summary>
    public ServerClose LastClose { get; private set; }

    /// <summary>The last connect attempt was refused with 401: the token is no longer accepted.</summary>
    public bool TokenRejected { get; private set; }

    public SocketClient(string baseUrl)
    {
        _baseUrl = baseUrl;
        _dispatch = Task.Run(DispatchLoopAsync);
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

        LastClose = ServerClose.Other;
        TokenRejected = false;

        var uri = new Uri($"{_baseUrl.TrimEnd('/')}/ws");
        _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        _webSocket.Options.AddSubProtocol(WebSocketBinding.Subprotocol);
        _webSocket.Options.CollectHttpResponseDetails = true;
        try
        {
            await _webSocket.ConnectAsync(uri, _cts.Token);
        }
        catch (WebSocketException)
        {
            TokenRejected = _webSocket.HttpStatusCode == HttpStatusCode.Unauthorized;
            throw;
        }
        OnConnected?.Invoke();
        
        _ = Task.Run(ReceiveLoopAsync, _cts.Token);
    }

    public async Task SendAsync(DeviceMessage message)
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
        using var reader = new WebSocketMessageReader(_webSocket, DeviceProtocol.MaxMessageBytes);
        try
        {
            while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(_cts.Token);
                switch (read.Kind)
                {
                    case FrameKind.Closed:
                        LastClose = ServerCloses.From(_webSocket.CloseStatus);
                        await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", _cts.Token);
                        return;
                    case FrameKind.TooBig:
                        // Oversized message; drop the connection and let the reconnect monitor recover.
                        return;
                    case FrameKind.Binary:
                        continue;
                }

                await HandleFrameAsync(read.Text!);
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

    private async Task HandleFrameAsync(string text)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || typeElement.GetString() is not { Length: > 0 } type)
            {
                return;
            }

            if (MessageTypes.Comparer.Equals(type, MessageTypes.Ping))
            {
                await SendAsync(new DeviceMessage(MessageTypes.Pong, ReadPing(root)));
                return;
            }

            if (MessageTypes.IsReserved(type) && !MessageTypes.IsPlatformCommand(type))
            {
                return;
            }

            var payload = root.TryGetProperty("payload", out var p) ? (object)p.Clone() : null;
            var commandId = root.TryGetProperty("commandId", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            if (commandId != null)
            {
                if (CommandExpiry.IsExpired(root, DateTimeOffset.UtcNow))
                {
                    // Answered, so the platform reports it as refused rather than lost.
                    await SendAsync(new DeviceMessage(MessageTypes.Ack, new AckPayload(commandId, type, AckPayload.Expired)));
                    return;
                }

                // Transport-level delivery ack; app-level acks are ordinary messages.
                await SendAsync(new DeviceMessage(MessageTypes.Ack, new AckPayload(commandId)));
            }

            // Handed to the app on its own task, so a slow or throwing handler neither delays pongs nor ends the connection.
            _inbox.Writer.TryWrite(new InboundMessage(type, payload, commandId));
        }
    }

    private static PongPayload ReadPing(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new PongPayload(default);
        }

        var timestamp = payload.TryGetProperty("timestamp", out var t) && t.TryGetDateTime(out var at) ? at : default;
        var id = payload.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        return new PongPayload(timestamp, id);
    }

    private async Task DispatchLoopAsync()
    {
        try
        {
            await foreach (var message in _inbox.Reader.ReadAllAsync(_dispatchCts.Token))
            {
                try
                {
                    OnMessageReceived?.Invoke(message.Type, message.Payload, message.CommandId);
                }
                catch (Exception error)
                {
                    try
                    {
                        OnHandlerError?.Invoke(error);
                    }
                    catch
                    {
                        // An error handler that throws is ignored too.
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
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
        _inbox.Writer.TryComplete();
        await _dispatchCts.CancelAsync();
        await _dispatch;
        _webSocket.Dispose();
        _cts.Dispose();
        _dispatchCts.Dispose();
        _sendLock.Dispose();
    }
}

internal sealed record InboundMessage(string Type, object? Payload, string? CommandId);
