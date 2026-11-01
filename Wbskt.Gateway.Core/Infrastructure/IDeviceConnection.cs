using Wbskt.Protocol;

namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>
/// One device's live connection, whatever transport carries it. The gateway core holds devices
/// through this and never touches the transport: sending a message, pinging and closing are all the
/// connection's own business. Its transport's receive loop owns its end, and alone unregisters it.
/// </summary>
public interface IDeviceConnection
{
    int ClientId { get; }

    int WorkspaceId { get; }

    /// <summary>
    /// Used by ConnectionSupersededHandler to decide which of two concurrent connections for the same
    /// client (one per gateway instance) is the older one to close.
    /// </summary>
    DateTime EstablishedAtUtc { get; }

    /// <summary>Whether messages can be sent to the device now.</summary>
    bool IsOpen { get; }

    /// <summary>The close someone asked for, if any; the receive loop reports its reason.</summary>
    CloseRequest? CloseRequest { get; }

    Task SendAsync(SocketMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a <c>sys.ping</c> and remembers it, so the pong is timed on this host's clock rather
    /// than by a timestamp the device echoes back. A ping still unanswered is forgotten.
    /// </summary>
    Task SendPingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Matches a pong to the ping waiting for it and returns when that ping was sent. A pong with an
    /// id must carry the pending ping's; one without (SDKs before the ping id) answers the pending
    /// ping. Either way a ping is answered once.
    /// </summary>
    bool TryCompletePing(string? id, out DateTime sentAt);

    /// <summary>
    /// Asks the connection to close for <paramref name="reason"/>, which the transport turns into its
    /// own close (a WebSocket close code, for one). Only the first request counts; returns false for
    /// later ones.
    /// </summary>
    Task<bool> RequestCloseAsync(DisconnectReason reason, string description);
}
