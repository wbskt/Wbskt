using System.Net.WebSockets;
using Wbskt.Protocol;

namespace Wbskt.Client.Sdk.Internal;

/// <summary>Why the gateway ended the last connection, as far as the reconnect schedule cares.</summary>
internal enum ServerClose
{
    /// <summary>Anything else: reconnect on the usual schedule.</summary>
    Other,
    Restarting,
    Superseded,
    Revoked,
    Throttled
}

internal static class ServerCloses
{
    // 1008 is what a gateway from before the per-reason codes sends for all three; it stays Other.
    public static ServerClose From(WebSocketCloseStatus? status) => (int?)status switch
    {
        (int)WebSocketCloseStatus.EndpointUnavailable => ServerClose.Restarting,
        WebSocketBinding.SupersededCloseCode => ServerClose.Superseded,
        WebSocketBinding.RevokedCloseCode => ServerClose.Revoked,
        WebSocketBinding.ThrottledCloseCode => ServerClose.Throttled,
        _ => ServerClose.Other
    };
}
