using Wbskt.Protocol;

namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>
/// One client's live connection, whatever transport carries it. The gateway core holds clients
/// through this and never touches the transport: sending a message, pinging and closing are all the
/// connection's own business. Its session (<see cref="Services.ClientSessions.RunAsync"/>) owns its
/// end, and alone unregisters it.
/// </summary>
/// <remarks>
/// A transport must cap each message it reads at <see cref="Wbskt.Protocol.ClientProtocol.MaxMessageBytes"/>
/// before buffering more, and end the session as <see cref="DisconnectReason.MessageTooBig"/> past it;
/// the inbox checks the same limit again, but by then the transport has already held the bytes.
/// </remarks>
public interface IClientConnection
{
    int ClientId { get; }

    int WorkspaceId { get; }

    /// <summary>
    /// Used by ConnectionSupersededHandler to decide which of two concurrent connections for the same
    /// client (one per gateway instance) is the older one to close.
    /// </summary>
    DateTime EstablishedAtUtc { get; }

    /// <summary>Whether messages can be sent to the client now.</summary>
    bool IsOpen { get; }

    /// <summary>The close someone asked for, if any; the receive loop reports its reason.</summary>
    CloseRequest? CloseRequest { get; }

    Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a <c>sys.ping</c> and remembers it, so the pong is timed on this host's clock rather
    /// than by a timestamp the client echoes back. A ping still unanswered is forgotten.
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

    /// <summary>
    /// Closes the transport for the session's final <paramref name="reason"/>, if it is still open.
    /// Called once by the session, after the client is released and before its end is announced; it
    /// should not throw, and must not wait long on a client that does not answer.
    /// </summary>
    Task CloseTransportAsync(DisconnectReason reason);
}
