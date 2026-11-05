namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>Why a connection ended. One value per connection, recorded by its receive loop alone.</summary>
public enum DisconnectReason
{
    /// <summary>The client sent a Close frame, the normal end of a session.</summary>
    ClientClosed,

    /// <summary>The connection dropped without a Close: network loss, a killed process, a proxy timeout.</summary>
    Aborted,

    /// <summary>This instance is shutting down: a deploy or a restart, not a client problem.</summary>
    ServerStopping,

    /// <summary>The receive loop failed unexpectedly.</summary>
    Error,

    /// <summary>The client lost access: revoked, deleted, or its secret rotated.</summary>
    Revoked,

    /// <summary>The client connected again, here or on another host, and the newer connection won.</summary>
    Superseded,

    /// <summary>The client sent a message over the size limit.</summary>
    MessageTooBig,

    /// <summary>The client kept sending faster than its message budget allows.</summary>
    Throttled,

    /// <summary>The client closed to install a firmware update, and is expected back on the new image.</summary>
    Updating
}

/// <summary>
/// A close asked of a connection by someone other than its receive loop. The transport decides how
/// the reason reaches the client (a WebSocket close code, for one).
/// </summary>
public sealed record CloseRequest(DisconnectReason Reason, string Description);

public static class DisconnectReasons
{
    /// <summary>The metric tag value (<c>wbskt_socket_disconnects_total{reason}</c>).</summary>
    public static string Tag(this DisconnectReason reason) => reason switch
    {
        DisconnectReason.ClientClosed => "client_closed",
        DisconnectReason.Aborted => "aborted",
        DisconnectReason.ServerStopping => "server_stopping",
        DisconnectReason.Error => "error",
        DisconnectReason.Revoked => "revoked",
        DisconnectReason.Superseded => "superseded",
        DisconnectReason.MessageTooBig => "message_too_big",
        DisconnectReason.Throttled => "throttled",
        DisconnectReason.Updating => "updating",
        _ => "unknown"
    };

    /// <summary>What <c>ClientDisconnectedEvent.Reason</c> says, for the console and the event log.</summary>
    public static string Describe(this DisconnectReason reason) => reason switch
    {
        DisconnectReason.ClientClosed => "Closed by the client.",
        DisconnectReason.Aborted => "The connection was lost.",
        DisconnectReason.ServerStopping => "The server restarted.",
        DisconnectReason.Error => "The server hit an error.",
        DisconnectReason.Revoked => "The client lost access.",
        DisconnectReason.Superseded => "The client connected again.",
        DisconnectReason.MessageTooBig => "The client sent a message over the size limit.",
        DisconnectReason.Throttled => "The client sent messages faster than allowed.",
        DisconnectReason.Updating => "The client is installing a firmware update.",
        _ => "Disconnected."
    };
}
