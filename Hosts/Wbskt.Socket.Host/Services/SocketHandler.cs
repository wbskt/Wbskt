using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Wbskt.Client.Sdk.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Security;
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
    private readonly IClientTokenCutoffs _cutoffs;
    private readonly ILogger<SocketHandler> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IEventBus _eventBus;
    private readonly SocketMetrics _metrics;
    private readonly string _hostId;

    public SocketHandler(
        IConnectionManager connectionManager,
        IRevocationCache revocationCache,
        IClientTokenCutoffs cutoffs,
        ILogger<SocketHandler> logger,
        IHostApplicationLifetime appLifetime,
        IEventBus eventBus,
        BusInstanceId busInstanceId,
        SocketMetrics metrics)
    {
        _metrics = metrics;
        _connectionManager = connectionManager;
        _revocationCache = revocationCache;
        _cutoffs = cutoffs;
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

        // A revoked client's JWT stays valid for up to an hour; the deny-list closes that window. A
        // token without a readable issue time is treated as older than any cutoff. This host's list
        // is filled by events and lost on restart, so the cutoff the management host keeps in Redis
        // is asked too; the list alone decides only when Redis cannot answer.
        var issuedAt = long.TryParse(context.User.FindFirst("iat")?.Value, out var iat)
            ? DateTimeOffset.FromUnixTimeSeconds(iat).UtcDateTime
            : DateTime.MinValue;
        if (_revocationCache.IsRevoked(clientRefId, issuedAt)
            || await _cutoffs.IsRevokedAsync(clientRefId, issuedAt, cts.Token) == true)
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
            // Not tied to the connection: a client that drops straight away must still be recorded as
            // having connected, or its disconnect has nothing to pair with.
            await _eventBus.PublishAsync(new ClientConnectedEvent(clientRefId, clientId, workspaceId, _hostId), CancellationToken.None);

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
                await _eventBus.PublishAsync(new ClientMessageReceivedEvent(clientRefId, connection.ClientId, connection.WorkspaceId, message.Type, payload)
                {
                    SentAtUtc = PlausibleSentAt(message.SentAt, DateTime.UtcNow)
                }, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Invalid JSON received from client {ClientRefId}: {Error}", clientRefId, ex.Message);
            }
        }
    }

    internal static readonly TimeSpan MaxSentAtAge = TimeSpan.FromDays(7);
    internal static readonly TimeSpan MaxSentAtClockAhead = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The device's send time, if it is believable: not in the future beyond small clock drift and
    /// no older than a week. Anything else is dropped so a wrong device clock cannot rewrite history.
    /// </summary>
    internal static DateTime? PlausibleSentAt(DateTimeOffset? sentAt, DateTime nowUtc)
    {
        if (sentAt is not { } value)
        {
            return null;
        }

        var utc = value.UtcDateTime;
        if (utc > nowUtc + MaxSentAtClockAhead || utc < nowUtc - MaxSentAtAge)
        {
            return null;
        }

        // A device clock slightly ahead still means "now".
        return utc > nowUtc ? nowUtc : utc;
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
                    // An SDK that refuses a command (it arrived past its expiresAt) still answers, so
                    // the sender sees a failure instead of a command that was delivered and never acted on.
                    if (CommandRefusal.TryRead(message.Payload, out var refusedType, out var reason))
                    {
                        await _eventBus.PublishAsync(new ClientCommandFailedEvent(clientRefId, connection.ClientId, connection.WorkspaceId, refusedType, reason, commandId), cancellationToken);
                    }
                    else
                    {
                        await _eventBus.PublishAsync(new ClientCommandAckedEvent(clientRefId, connection.ClientId, connection.WorkspaceId, commandId), cancellationToken);
                    }
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
