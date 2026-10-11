namespace Wbskt.Protocol;

/// <summary>
/// The message types the platform itself uses on the socket. Every type starting with
/// <see cref="SystemPrefix"/> is reserved: devices cannot send their own, and the server never
/// passes one on to workflow triggers or the device-message log.
/// </summary>
public static class MessageTypes
{
    public const string SystemPrefix = "sys.";

    /// <summary>Server to device: answer with <see cref="Pong"/>. Payload: <see cref="PingPayload"/>.</summary>
    public const string Ping = "sys.ping";

    /// <summary>Device to server, answering a <see cref="Ping"/>. Payload: <see cref="PongPayload"/>.</summary>
    public const string Pong = "sys.pong";

    /// <summary>Device to server: a command carrying a commandId arrived. Payload: <see cref="AckPayload"/>.</summary>
    public const string Ack = "sys.ack";

    /// <summary>Message types longer than this are refused.</summary>
    public const int MaxTypeLength = 100;

    public static bool IsSystem(string type) => type.StartsWith(SystemPrefix, StringComparison.Ordinal);
}

/// <summary>Limits and the version every side of the socket agrees on.</summary>
public static class SocketProtocol
{
    /// <summary>
    /// The WebSocket subprotocol (<c>Sec-WebSocket-Protocol</c>) of this version of the protocol. A
    /// client offers it, and the server accepts it; a connection that offers none is treated as v1.
    /// </summary>
    public const string V1 = "wbskt.v1";

    /// <summary>Messages are assembled across frames up to this size; a larger one ends the connection.</summary>
    public const int MaxMessageBytes = 64 * 1024;
}
