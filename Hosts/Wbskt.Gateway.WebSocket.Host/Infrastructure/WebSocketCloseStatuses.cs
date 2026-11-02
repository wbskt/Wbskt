using System.Net.WebSockets;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.WebSocket.Host.Infrastructure;

/// <summary>The WebSocket close code a device receives when this host closes its connection.</summary>
public static class WebSocketCloseStatuses
{
    /// <summary>
    /// For any end of a connection: 1009 for an oversized message, 1008 for lost access, a newer
    /// connection or flooding, 1001 for a restart, 1011 for a server fault. These codes are part of the
    /// device protocol; SDKs act on them.
    /// </summary>
    public static WebSocketCloseStatus For(DisconnectReason reason) => reason switch
    {
        DisconnectReason.MessageTooBig => WebSocketCloseStatus.MessageTooBig,
        DisconnectReason.Revoked or DisconnectReason.Superseded or DisconnectReason.Throttled => WebSocketCloseStatus.PolicyViolation,
        DisconnectReason.ServerStopping => WebSocketCloseStatus.EndpointUnavailable,
        DisconnectReason.Error => WebSocketCloseStatus.InternalServerError,
        _ => WebSocketCloseStatus.NormalClosure
    };
}
