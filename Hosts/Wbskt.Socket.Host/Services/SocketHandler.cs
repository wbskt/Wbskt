using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Client.Sdk.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure.Security;
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

    // Message types longer than this are refused; the same cap CommandRefusal puts on a refused type.
    internal const int MaxTypeLength = 100;

    private readonly IConnectionManager _connectionManager;
    private readonly IClientAccessGate _accessGate;
    private readonly ILogger<SocketHandler> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IEventBus _eventBus;
    private readonly SocketMetrics _metrics;
    private readonly MessageBudgetOptions _budgetOptions;
    private readonly string _hostId;

    public SocketHandler(
        IConnectionManager connectionManager,
        IClientAccessGate accessGate,
        ILogger<SocketHandler> logger,
        IHostApplicationLifetime appLifetime,
        IEventBus eventBus,
        BusInstanceId busInstanceId,
        SocketMetrics metrics,
        IOptions<MessageBudgetOptions> budgetOptions)
    {
        _budgetOptions = budgetOptions.Value;
        _metrics = metrics;
        _connectionManager = connectionManager;
        _accessGate = accessGate;
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

        // The ClientOnly policy on /ws has already authenticated a device token; one that is not well
        // formed (a claim missing or unreadable) is refused here rather than failing later.
        if (!ClientIdentity.TryRead(context.User, out var identity))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var (clientRefId, clientId, workspaceId, issuedAt) = identity;

        // A revoked client's JWT stays valid for up to an hour; the access gate closes that window.
        if (await _accessGate.IsRefusedAsync(clientRefId, issuedAt, context.RequestAborted))
        {
            _logger.LogWarning("Rejecting websocket for revoked client {ClientRefId}", clientRefId);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        using var connection = new ClientConnection
        {
            Socket = webSocket,
            ClientId = clientId,
            WorkspaceId = workspaceId
        };

        // Ends the receive loop when the device goes away, the host stops, or a close someone asked
        // for (see ClientConnection.RequestCloseAsync) is not answered in time.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            _appLifetime.ApplicationStopping,
            connection.Closing);

        // The newest connection wins: one this host still holds for the client is closed as superseded.
        _connectionManager.Register(clientRefId, connection);
        _logger.LogInformation("Client {ClientRefId} connected.", clientRefId);
        _metrics.Connected();
        var reason = DisconnectReason.ClientClosed;

        try
        {
            // Not tied to the connection: a client that drops straight away must still be recorded as
            // having connected, or its disconnect has nothing to pair with.
            await _eventBus.PublishAsync(new ClientConnectedEvent(clientRefId, clientId, workspaceId, _hostId), CancellationToken.None);

            reason = await ReceiveLoopAsync(clientRefId, connection, cts.Token);
        }
        catch (OperationCanceledException)
        {
            reason = _appLifetime.ApplicationStopping.IsCancellationRequested ? DisconnectReason.ServerStopping : DisconnectReason.Aborted;
        }
        catch (WebSocketException ex)
        {
            reason = DisconnectReason.Aborted;
            _logger.LogDebug("WebSocket error for client {ClientRefId}: {Message}", clientRefId, ex.Message);
        }
        catch (Exception ex)
        {
            reason = DisconnectReason.Error;
            _logger.LogError(ex, "Unexpected error in WebSocket loop for client {ClientRefId}.", clientRefId);
        }
        finally
        {
            // A close someone asked for is the real reason, whatever the loop saw as it ended.
            reason = connection.CloseRequest?.Reason ?? reason;
            _connectionManager.Unregister(clientRefId, connection);
            await CloseQuietlyAsync(webSocket, reason);
            _metrics.Disconnected(reason);
            _logger.LogInformation("Client {ClientRefId} disconnected ({Reason}).", clientRefId, reason.Tag());

            // A superseded connection is not the device going offline: its newer connection, here or on
            // another host, already announced it as connected.
            if (reason != DisconnectReason.Superseded)
            {
                var description = connection.CloseRequest?.Description ?? reason.Describe();
                await _eventBus.PublishAsync(new ClientDisconnectedEvent(clientRefId, clientId, workspaceId, description, _hostId), CancellationToken.None);
            }
        }
    }

    private static async Task CloseQuietlyAsync(WebSocket webSocket, DisconnectReason reason)
    {
        if (webSocket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            var status = reason == DisconnectReason.ServerStopping ? WebSocketCloseStatus.EndpointUnavailable : WebSocketCloseStatus.NormalClosure;
            using var timeout = new CancellationTokenSource(ClientConnection.CloseGrace);
            await webSocket.CloseOutputAsync(status, reason.Describe(), timeout.Token);
        }
        catch
        {
            // The connection is going away regardless.
        }
    }

    private async Task<DisconnectReason> ReceiveLoopAsync(Guid clientRefId, ClientConnection connection, CancellationToken cancellationToken)
    {
        var webSocket = connection.Socket;
        var buffer = new byte[1024 * 4];
        using var messageBuffer = new MemoryStream();
        var budget = new MessageBudget(_budgetOptions);

        while (webSocket.State is WebSocketState.Open or WebSocketState.CloseSent && !cancellationToken.IsCancellationRequested)
        {
            // A logical message can span multiple frames; assemble until EndOfMessage.
            messageBuffer.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Either the device closed, or it answered a close we sent (the handshake is then complete).
                    if (webSocket.State == WebSocketState.CloseReceived)
                    {
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", cancellationToken);
                    }

                    return DisconnectReason.ClientClosed;
                }

                if (messageBuffer.Length + result.Count > MaxMessageBytes)
                {
                    _logger.LogWarning("Client {ClientRefId} exceeded the {MaxMessageBytes}-byte message limit; closing.", clientRefId, MaxMessageBytes);
                    await connection.RequestCloseAsync(DisconnectReason.MessageTooBig, WebSocketCloseStatus.MessageTooBig, $"Messages are limited to {MaxMessageBytes} bytes.");
                    return DisconnectReason.MessageTooBig;
                }

                messageBuffer.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            // After we sent a Close, anything but the device's own Close is ignored.
            if (result.MessageType != WebSocketMessageType.Text || connection.CloseRequest != null)
            {
                continue;
            }

            // Every message becomes broker traffic (event log, live feed, workflow triggers), so a
            // device over its budget loses the excess, and one that keeps on is disconnected.
            if (!budget.TryTake())
            {
                _metrics.Dropped("throttled");
                if (budget.ConsecutiveDropped == 1)
                {
                    _logger.LogWarning("Client {ClientRefId} is sending faster than its message budget; dropping the excess.", clientRefId);
                }

                if (budget.Exhausted)
                {
                    _logger.LogWarning("Client {ClientRefId} stayed over its message budget for {Dropped} messages; closing.", clientRefId, budget.ConsecutiveDropped);
                    await connection.RequestCloseAsync(DisconnectReason.Throttled, WebSocketCloseStatus.PolicyViolation, "Too many messages.");
                    return DisconnectReason.Throttled;
                }

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

                // The type routes the message and names it in the event log and in triggers.
                if (string.IsNullOrWhiteSpace(message.Type) || message.Type.Length > MaxTypeLength)
                {
                    _metrics.Dropped("invalid");
                    _logger.LogDebug("Dropping a message from {ClientRefId} with a missing or over-long type.", clientRefId);
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

        return DisconnectReason.Aborted;
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
