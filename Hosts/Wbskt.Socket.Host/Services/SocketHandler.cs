using System.Net.WebSockets;
using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Protocol;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;
using Wbskt.Socket.Host.Services.Inbound;
using Wbskt.Socket.Host.Telemetry;

namespace Wbskt.Socket.Host.Services;

public interface ISocketHandler
{
    Task HandleAsync(HttpContext context);
}

internal sealed class SocketHandler : ISocketHandler
{
    // Messages are assembled across frames up to this cap; larger senders are disconnected.
    private const int MaxMessageBytes = SocketProtocol.MaxMessageBytes;

    private readonly IConnectionManager _connectionManager;
    private readonly IClientAccessGate _accessGate;
    private readonly ILogger<SocketHandler> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IEventBus _eventBus;
    private readonly InboundMessageDispatcher _dispatcher;
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
        IOptions<MessageBudgetOptions> budgetOptions,
        InboundMessageDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
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
            await Problems.WriteAsync(context, StatusCodes.Status400BadRequest, "SOCKET_UPGRADE_REQUIRED", "/ws accepts WebSocket requests only.");
            return;
        }

        // The ClientOnly policy on /ws has already authenticated a device token; one that is not well
        // formed (a claim missing or unreadable) is refused here rather than failing later.
        if (!ClientIdentity.TryRead(context.User, out var identity))
        {
            await Problems.WriteAsync(context, StatusCodes.Status401Unauthorized, "CLIENT_TOKEN_INVALID", "The device token is missing a claim this host needs.");
            return;
        }

        var (clientRefId, clientId, workspaceId, issuedAt) = identity;

        // A revoked client's JWT stays valid for up to an hour; the access gate closes that window.
        if (await _accessGate.IsRefusedAsync(clientRefId, issuedAt, context.RequestAborted))
        {
            _logger.LogWarning("Rejecting websocket for revoked client {ClientRefId}", clientRefId);
            await Problems.WriteAsync(context, StatusCodes.Status403Forbidden, "CLIENT_REVOKED", "This device's access has been revoked.");
            return;
        }

        // The protocol version: a client offers wbskt.v1, and one that offers nothing (SDKs before the
        // version, a browser that sets no subprotocol) is spoken to as v1. One that offers only
        // versions this host does not speak is refused before the upgrade.
        var offered = context.WebSockets.WebSocketRequestedProtocols;
        if (offered.Count > 0 && !offered.Contains(SocketProtocol.V1))
        {
            _logger.LogWarning("Rejecting websocket for client {ClientRefId}: no supported protocol in {Offered}.", clientRefId, string.Join(", ", offered));
            await Problems.WriteAsync(context, StatusCodes.Status400BadRequest, "SOCKET_PROTOCOL_UNSUPPORTED", $"Offer the {SocketProtocol.V1} subprotocol, or none.");
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync(offered.Count > 0 ? SocketProtocol.V1 : null);
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
        using var reader = new WebSocketMessageReader(webSocket, MaxMessageBytes);
        var budget = new MessageBudget(_budgetOptions);
        var inbound = new InboundContext(clientRefId, connection);

        while (webSocket.State is WebSocketState.Open or WebSocketState.CloseSent && !cancellationToken.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(cancellationToken);
            switch (read.Kind)
            {
                case FrameKind.Closed:
                    // Either the device closed, or it answered a close we sent (the handshake is then complete).
                    if (webSocket.State == WebSocketState.CloseReceived)
                    {
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", cancellationToken);
                    }

                    return DisconnectReason.ClientClosed;
                case FrameKind.TooBig:
                    _logger.LogWarning("Client {ClientRefId} exceeded the {MaxMessageBytes}-byte message limit; closing.", clientRefId, MaxMessageBytes);
                    await connection.RequestCloseAsync(DisconnectReason.MessageTooBig, WebSocketCloseStatus.MessageTooBig, $"Messages are limited to {MaxMessageBytes} bytes.");
                    return DisconnectReason.MessageTooBig;
                case FrameKind.Binary:
                    continue;
            }

            // After we sent a Close, anything but the device's own Close is ignored.
            if (connection.CloseRequest != null)
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

            _logger.LogDebug("Received from {ClientRefId}: {Message}", clientRefId, read.Text);
            await _dispatcher.DispatchAsync(inbound, read.Text!, cancellationToken);
        }

        return DisconnectReason.Aborted;
    }
}
