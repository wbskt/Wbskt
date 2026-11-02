namespace Wbskt.Protocol;

/// <summary>
/// The message types the platform itself uses on the socket. Every type starting with
/// <see cref="SystemPrefix"/>, in any case, is reserved: devices cannot send their own, and the server
/// never passes one on to workflow triggers or the device-message log. <see cref="Capabilities"/> and
/// <see cref="StateReport"/> are ordinary device-to-server messages that the server also reads, so
/// nothing else may be sent to a device under those names.
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

    /// <summary>
    /// Server to device: install the firmware at a URL. The one platform message that is a command: it
    /// carries a commandId and an expiresAt, and is acked and refused when expired like any other.
    /// Payload: <see cref="OtaUpdatePayload"/>. Sent only to a device that lists
    /// <see cref="SystemCapabilities.Ota"/> in its capabilities.
    /// </summary>
    public const string OtaUpdate = "sys.ota.update";

    /// <summary>Device to server: how a <see cref="OtaUpdate"/> is going. Payload: <see cref="OtaStatusPayload"/>.</summary>
    public const string OtaStatus = "sys.ota.status";

    /// <summary>Device to server: what it is and which commands it takes. Payload: <see cref="ClientCapabilities"/>.</summary>
    public const string Capabilities = "capabilities";

    /// <summary>Device to server: a JSON object of state variables that changed, by name.</summary>
    public const string StateReport = "state.report";

    /// <summary>Message types longer than this are refused.</summary>
    public const int MaxTypeLength = 100;

    /// <summary>
    /// Message types compare ignoring case wherever the platform decides something by them, so
    /// <c>SYS.ping</c> is as reserved as <c>sys.ping</c> on every host.
    /// </summary>
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static bool IsSystem(string type) => type.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>A reserved type the platform sends as a command, through the same path as a device's own commands.</summary>
    public static bool IsPlatformCommand(string type) => Comparer.Equals(type, OtaUpdate);

    /// <summary>A type only the platform may send to a device: a system type, or one the server reads from devices.</summary>
    public static bool IsReserved(string type) =>
        IsSystem(type) || Comparer.Equals(type, Capabilities) || Comparer.Equals(type, StateReport);
}

/// <summary>Limits of the message model that every binding and every SDK agrees on.</summary>
public static class DeviceProtocol
{
    /// <summary>A message, assembled from whatever pieces the binding carries it in, is at most this size; a larger one ends the connection.</summary>
    public const int MaxMessageBytes = 64 * 1024;

    /// <summary>
    /// Messages a second an SDK sends at most, a flushed offline backlog included. Below the gateway's
    /// default budget (10 a second, burst 100) so a device never relies on the gateway's headroom.
    /// </summary>
    public const double SendPerSecond = 8;

    /// <summary>Messages an SDK sends back to back before <see cref="SendPerSecond"/> applies.</summary>
    public const int SendBurst = 50;
}
