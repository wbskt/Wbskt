using System.Net.WebSockets;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Protocol;

namespace Wbskt.Gateway.WebSocket.Host.Infrastructure;

/// <summary>The WebSocket close code a device receives when this host closes its connection.</summary>
public static class WebSocketCloseStatuses
{
    /// <summary>Superseded: a newer connection for the same device replaced this one.</summary>
    public const WebSocketCloseStatus Superseded = (WebSocketCloseStatus)WebSocketBinding.SupersededCloseCode;

    /// <summary>Revoked: the device lost access (revoked, deleted or its secret rotated).</summary>
    public const WebSocketCloseStatus Revoked = (WebSocketCloseStatus)WebSocketBinding.RevokedCloseCode;

    /// <summary>Throttled: the device kept sending over its message budget.</summary>
    public const WebSocketCloseStatus Throttled = (WebSocketCloseStatus)WebSocketBinding.ThrottledCloseCode;

    /// <summary>
    /// For any end of a connection: 4001–4003 for a newer connection, lost access and flooding, 1009
    /// for an oversized message, 1001 for a restart, 1011 for a server fault. These codes are part of
    /// the device protocol; SDKs act on them.
    /// </summary>
    public static WebSocketCloseStatus For(DisconnectReason reason) => reason switch
    {
        DisconnectReason.MessageTooBig => WebSocketCloseStatus.MessageTooBig,
        DisconnectReason.Superseded => Superseded,
        DisconnectReason.Revoked => Revoked,
        DisconnectReason.Throttled => Throttled,
        DisconnectReason.ServerStopping => WebSocketCloseStatus.EndpointUnavailable,
        DisconnectReason.Error => WebSocketCloseStatus.InternalServerError,
        _ => WebSocketCloseStatus.NormalClosure
    };
}
