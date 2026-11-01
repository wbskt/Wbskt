using System.Net.WebSockets;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Protocol;
using Wbskt.Gateway.WebSocket.Host.Infrastructure;

namespace Wbskt.Gateway.WebSocket.Host.Services;

public interface ISocketHandler
{
    Task HandleAsync(HttpContext context);
}

/// <summary>
/// The WebSocket transport: the upgrade, the subprotocol, the frame loop and the close. Who may
/// connect and what a session means (registering the device, announcing it, budgeting and routing its
/// messages, recording its end) is Gateway.Core's <see cref="DeviceSessions"/>.
/// </summary>
internal sealed class SocketHandler : ISocketHandler
{
    // Messages are assembled across frames up to this cap; larger senders are disconnected.
    private const int MaxMessageBytes = SocketProtocol.MaxMessageBytes;

    private readonly DeviceSessions _sessions;
    private readonly ILogger<SocketHandler> _logger;
    private readonly IHostApplicationLifetime _appLifetime;

    public SocketHandler(
        DeviceSessions sessions,
        ILogger<SocketHandler> logger,
        IHostApplicationLifetime appLifetime)
    {
        _sessions = sessions;
        _logger = logger;
        _appLifetime = appLifetime;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await Problems.WriteAsync(context, StatusCodes.Status400BadRequest, "SOCKET_UPGRADE_REQUIRED", "/ws accepts WebSocket requests only.");
            return;
        }

        // The ClientOnly policy on /ws has already authenticated a device token. Whether this device may
        // connect (a well-formed token, not revoked since it was issued) is the session's to decide.
        var admission = await _sessions.AdmitAsync(context.User, context.RequestAborted);
        if (!admission.IsAdmitted)
        {
            await RefuseAsync(context, admission.Refusal.Value);
            return;
        }

        var identity = admission.Identity;

        // The protocol version: a client offers wbskt.v1, and one that offers nothing (SDKs before the
        // version, a browser that sets no subprotocol) is spoken to as v1. One that offers only
        // versions this host does not speak is refused before the upgrade.
        var offered = context.WebSockets.WebSocketRequestedProtocols;
        if (offered.Count > 0 && !offered.Contains(SocketProtocol.V1))
        {
            _logger.LogWarning("Rejecting websocket for client {ClientRefId}: no supported protocol in {Offered}.", identity.ClientRefId, string.Join(", ", offered));
            await Problems.WriteAsync(context, StatusCodes.Status400BadRequest, "SOCKET_PROTOCOL_UNSUPPORTED", $"Offer the {SocketProtocol.V1} subprotocol, or none.");
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync(offered.Count > 0 ? SocketProtocol.V1 : null);
        using var connection = new ClientConnection
        {
            Socket = webSocket,
            ClientId = identity.ClientId,
            WorkspaceId = identity.WorkspaceId
        };

        // Ends the receive loop when the device goes away, the host stops, or a close someone asked
        // for (see ClientConnection.CloseAsync) is not answered in time.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            _appLifetime.ApplicationStopping,
            connection.Closing);

        await _sessions.RunAsync(
            identity,
            connection,
            inbox => ReceiveLoopAsync(identity.ClientRefId, connection, inbox, cts.Token),
            reason => CloseQuietlyAsync(webSocket, reason));
    }

    private static Task RefuseAsync(HttpContext context, AdmissionRefusal refusal) => refusal switch
    {
        AdmissionRefusal.Revoked => Problems.WriteAsync(context, StatusCodes.Status403Forbidden, "CLIENT_REVOKED", "This device's access has been revoked."),
        _ => Problems.WriteAsync(context, StatusCodes.Status401Unauthorized, "CLIENT_TOKEN_INVALID", "The device token is missing a claim this host needs.")
    };

    private static async Task CloseQuietlyAsync(System.Net.WebSockets.WebSocket webSocket, DisconnectReason reason)
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

    private async Task<DisconnectReason> ReceiveLoopAsync(Guid clientRefId, ClientConnection connection, DeviceInbox inbox, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadFramesAsync(clientRefId, connection, inbox, cancellationToken);
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug("WebSocket error for client {ClientRefId}: {Message}", clientRefId, ex.Message);
            return DisconnectReason.Aborted;
        }
    }

    private async Task<DisconnectReason> ReadFramesAsync(Guid clientRefId, ClientConnection connection, DeviceInbox inbox, CancellationToken cancellationToken)
    {
        var webSocket = connection.Socket;
        using var reader = new WebSocketMessageReader(webSocket, MaxMessageBytes);

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
                    await connection.RequestCloseAsync(DisconnectReason.MessageTooBig, DeviceInbox.TooBigDescription);
                    return DisconnectReason.MessageTooBig;
                case FrameKind.Binary:
                    continue;
            }

            // The size backstop, the budget, the rate limit and routing are the session's; it says when
            // the device must go.
            if (await inbox.ReceiveAsync(read.Text!, cancellationToken) is { } ended)
            {
                return ended;
            }
        }

        return DisconnectReason.Aborted;
    }
}
