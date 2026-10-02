using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Wbskt.Client.Sdk.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;
using Wbskt.Socket.Host.Telemetry;

namespace Wbskt.Socket.Host.Services;

public interface ISocketHandler
{
    Task HandleAsync(HttpContext context);
}

internal sealed class SocketHandler : ISocketHandler
{
    // Messages are assembled across frames up to this cap; larger senders are disconnected.
    private const int MaxMessageBytes = 64 * 1024;

    private readonly IConnectionManager _connectionManager;
    private readonly IRevocationCache _revocationCache;
    private readonly ILogger<SocketHandler> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IEventBus _eventBus;
    private readonly SocketMetrics _metrics;
    private readonly string _hostId;

    public SocketHandler(
        IConnectionManager connectionManager,
        IRevocationCache revocationCache,
        ILogger<SocketHandler> logger,
        IHostApplicationLifetime appLifetime,
        IEventBus eventBus,
        BusInstanceId busInstanceId,
        SocketMetrics metrics)
    {
        _metrics = metrics;
        _connectionManager = connectionManager;
        _revocationCache = revocationCache;
        _logger = logger;
        _appLifetime = appLifetime;
        _eventBus = eventBus;
        _hostId = busInstanceId.Value;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Create a linked token that triggers if the client leaves OR the server stops
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            _appLifetime.ApplicationStopping
        );

        var clientRefIdString = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var clientId = int.Parse(context.User.FindFirst("id")!.Value);
        var workspaceId = int.Parse(context.User.FindFirst("workspace_id")!.Value);
        if (!Guid.TryParse(clientRefIdString, out var clientRefId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // A revoked client's JWT stays valid for up to an hour; the deny-list closes that window.
        if (_revocationCache.IsRevoked(clientRefId))
        {
            _logger.LogWarning("Rejecting websocket for revoked client {ClientRefId}", clientRefId);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new ClientConnection
        {
            Socket = webSocket,
            ClientId = clientId,
            WorkspaceId = workspaceId
        };

        if (!_connectionManager.TryAddConnection(clientRefId, connection))
        {
            _logger.LogWarning("Rejecting duplicate websocket for client {ClientRefId}", clientRefId);
            await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Multiple concurrent connections are not allowed.", CancellationToken.None);
            return;
        }

        _logger.LogInformation("Client {ClientRefId} connected.", clientRefId);
        _metrics.Connected();
        var disconnectReason = SocketMetrics.ClientClosed;

        try
        {
            await _eventBus.PublishAsync(new ClientConnectedEvent(clientRefId, clientId, workspaceId, _hostId), cts.Token);

            // Use the HttpContext.RequestAborted token to detect when the underlying TCP connection is lost
            await ReceiveLoopAsync(clientRefId, connection, cts.Token);
        }
        catch (OperationCanceledException)
        {
            disconnectReason = _appLifetime.ApplicationStopping.IsCancellationRequested ? SocketMetrics.ServerStopping : SocketMetrics.Aborted;
            _logger.LogInformation("Connection for client {ClientRefId} was cancelled.", clientRefId);
        }
        catch (WebSocketException ex)
        {
            disconnectReason = SocketMetrics.Aborted;
            _logger.LogWarning("WebSocket error for client {ClientRefId}: {Message}", clientRefId, ex.Message);
        }
        catch (Exception ex)
        {
            disconnectReason = SocketMetrics.Error;
            _logger.LogError(ex, "Unexpected error in WebSocket loop for client {ClientRefId}.", clientRefId);
        }
        finally
        {
            _metrics.Disconnected(disconnectReason);
            await _connectionManager.RemoveConnectionAsync(clientRefId, cancellationToken: CancellationToken.None);
            _logger.LogInformation("Client {ClientRefId} disconnected and cleaned up.", clientRefId);
            await _eventBus.PublishAsync(new ClientDisconnectedEvent(clientRefId, clientId, workspaceId, "Socket closed", _hostId), CancellationToken.None);
        }
    }

    private async Task ReceiveLoopAsync(Guid clientRefId, ClientConnection connection, CancellationToken cancellationToken)
    {
        var webSocket = connection.Socket;
        var buffer = new byte[1024 * 4];
        using var messageBuffer = new MemoryStream();

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            // A logical message can span multiple frames; assemble until EndOfMessage.
            messageBuffer.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Client {ClientRefId} initiated close.", clientRefId);
                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", cancellationToken);
                    return;
                }

                if (messageBuffer.Length + result.Count > MaxMessageBytes)
                {
                    _logger.LogWarning("Client {ClientRefId} exceeded the {MaxMessageBytes}-byte message limit; closing.", clientRefId, MaxMessageBytes);
                    await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig, $"Messages are limited to {MaxMessageBytes} bytes.", cancellationToken);
                    return;
                }

                messageBuffer.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var messageJson = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
            _logger.LogDebug("Received from {ClientRefId}: {Message}", clientRefId, messageJson);

            try
            {
                var message = JsonSerializer.Deserialize<SocketMessage>(messageJson);
                if (message == null)
                {
                    continue;
                }

                // Reserved protocol messages never reach the generic pipeline (workflow triggers, event log).
                if (message.Type.StartsWith("sys.", StringComparison.Ordinal))
                {
                    await HandleSystemMessageAsync(clientRefId, connection, message, cancellationToken);
                    continue;
                }

                var payload = JsonSerializer.Serialize(message.Payload);
                await _eventBus.PublishAsync(new ClientMessageReceivedEvent(clientRefId, connection.ClientId, connection.WorkspaceId, message.Type, payload), cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Invalid JSON received from client {ClientRefId}: {Error}", clientRefId, ex.Message);
            }
        }
    }

    private async Task HandleSystemMessageAsync(Guid clientRefId, ClientConnection connection, SocketMessage message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case "sys.pong":
                // RTT is measured here, on the socket, so bus hops never inflate the number.
                var originalPingTime = TryGetOriginalTimestamp(message.Payload) ?? connection.LastPingSentAt;
                if (originalPingTime == null)
                {
                    _logger.LogDebug("Received sys.pong from {ClientRefId} without a matching ping.", clientRefId);
                    return;
                }

                var roundTripMs = (DateTime.UtcNow - originalPingTime.Value).TotalMilliseconds;
                await _eventBus.PublishAsync(new ClientPongEvent(clientRefId, connection.ClientId, connection.WorkspaceId, originalPingTime.Value), cancellationToken);
                await _eventBus.PublishAsync(new ClientLatencyMeasuredEvent(clientRefId, connection.ClientId, connection.WorkspaceId, roundTripMs), cancellationToken);
                break;
            case "sys.ack":
                if (TryGetCommandId(message.Payload, out var commandId))
                {
                    await _eventBus.PublishAsync(new ClientCommandAckedEvent(clientRefId, connection.ClientId, connection.WorkspaceId, commandId), cancellationToken);
                }
                else
                {
                    _logger.LogDebug("Received sys.ack from {ClientRefId} without a command id.", clientRefId);
                }
                break;
            default:
                _logger.LogDebug("Ignoring unknown system message '{Type}' from {ClientRefId}.", message.Type, clientRefId);
                break;
        }
    }

    private static bool TryGetCommandId(object payload, out Guid commandId)
    {
        commandId = Guid.Empty;
        return payload is JsonElement element
            && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("commandId", out var id)
            && id.ValueKind == JsonValueKind.String
            && id.TryGetGuid(out commandId);
    }

    private static DateTime? TryGetOriginalTimestamp(object payload)
    {
        // The client echoes the server's own sys.ping timestamp back as 'originalTimestamp',
        // so the round trip is computed entirely on this host's clock.
        if (payload is JsonElement element
            && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("originalTimestamp", out var timestamp)
            && timestamp.ValueKind == JsonValueKind.String
            && timestamp.TryGetDateTime(out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }
}
