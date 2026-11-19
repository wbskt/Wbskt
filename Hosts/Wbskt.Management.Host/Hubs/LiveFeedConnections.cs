using System.Collections.Concurrent;

namespace Wbskt.Management.Host.Hubs;

/// <summary>The open <see cref="NotificationHub"/> connections on this host, by connection id.</summary>
public sealed class LiveFeedConnections
{
    private readonly ConcurrentDictionary<string, LiveFeedConnection> _open = new();

    public LiveFeedConnection Open(string connectionId, string? accessToken, Guid? userRef = null, Guid? sessionId = null, DateTimeOffset? issuedAt = null) =>
        _open.GetOrAdd(connectionId, id => new LiveFeedConnection(id, accessToken, userRef, sessionId, issuedAt));

    public LiveFeedConnection? Find(string connectionId) => _open.GetValueOrDefault(connectionId);

    public void Close(string connectionId) => _open.TryRemove(connectionId, out _);

    /// <summary>The connections that have joined at least one feed.</summary>
    public IReadOnlyCollection<LiveFeedConnection> Joined() =>
        _open.Values.Where(c => !c.Workspaces.IsEmpty || !c.Clients.IsEmpty).ToList();
}
