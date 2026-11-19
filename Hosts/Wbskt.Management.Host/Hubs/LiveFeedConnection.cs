using System.Collections.Concurrent;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// One console connection to <see cref="NotificationHub"/>: the token it connected with, and the
/// workspace and client feeds it has joined, so its access to each can be checked again after it joined.
/// </summary>
public sealed class LiveFeedConnection
{
    public LiveFeedConnection(string connectionId, string? accessToken, Guid? userRef = null, Guid? sessionId = null)
    {
        ConnectionId = connectionId;
        AccessToken = accessToken;
        UserRef = userRef;
        SessionId = sessionId;
    }

    public string ConnectionId { get; }

    /// <summary>The bearer token the connection was opened with; it lives no longer than that token.</summary>
    public string? AccessToken { get; }

    /// <summary>Whose token it is, so an access change naming other users passes it by; null when unknown.</summary>
    public Guid? UserRef { get; }

    /// <summary>The sign-in session the token belongs to, so ending another session passes it by; null when unknown.</summary>
    public Guid? SessionId { get; }

    /// <summary>Joined workspaces, by reference, with the internal id their group is named after.</summary>
    public ConcurrentDictionary<Guid, int> Workspaces { get; } = new();

    /// <summary>Watched clients, by reference, with their workspace and the internal id their group is named after.</summary>
    public ConcurrentDictionary<Guid, WatchedClient> Clients { get; } = new();

    /// <summary>Whether <paramref name="change"/> can reach any feed this connection has joined.</summary>
    public bool IsTouchedBy(AccessChange change) =>
        change.Touches(UserRef, SessionId, Workspaces.Keys.Concat(Clients.Values.Select(c => c.WorkspaceRef)));
}

/// <summary>A client whose feed a connection watches, and the workspace that let it.</summary>
public sealed record WatchedClient(Guid WorkspaceRef, int ClientId);
