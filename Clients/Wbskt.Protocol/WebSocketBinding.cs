namespace Wbskt.Protocol;

/// <summary>What is particular to carrying the message model over a WebSocket (Docs/Client.Protocol.WebSocket.md).</summary>
public static class WebSocketBinding
{
    /// <summary>
    /// The subprotocol (<c>Sec-WebSocket-Protocol</c>) of this version of the protocol. A client offers
    /// it, and the gateway accepts it; a connection that offers none is treated as v1.
    /// </summary>
    public const string Subprotocol = "wbskt.v1";

    /// <summary>
    /// The close code a client sends when it closes to install a firmware update. The gateway reports
    /// the client as updating rather than offline, for up to <see cref="OtaTiming.UpdateWindow"/>.
    /// </summary>
    public const int UpdatingCloseCode = 4000;

    /// <summary>The gateway closed this connection because a newer one for the same client replaced it.</summary>
    public const int SupersededCloseCode = 4001;

    /// <summary>The gateway closed this connection because the client lost access: drop the token before reconnecting.</summary>
    public const int RevokedCloseCode = 4002;

    /// <summary>The gateway closed this connection because the client kept sending over its message budget.</summary>
    public const int ThrottledCloseCode = 4003;
}
