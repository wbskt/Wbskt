namespace Wbskt.Protocol;

/// <summary>What is particular to carrying the message model over a WebSocket (Docs/Device.Protocol.WebSocket.md).</summary>
public static class WebSocketBinding
{
    /// <summary>
    /// The subprotocol (<c>Sec-WebSocket-Protocol</c>) of this version of the protocol. A client offers
    /// it, and the gateway accepts it; a connection that offers none is treated as v1.
    /// </summary>
    public const string Subprotocol = "wbskt.v1";
}
