using System.Collections.Concurrent;
using Wbskt.Infrastructure.Billing;

namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>
/// The workspaces whose clients are held to one message a minute, because their account has carried as
/// many messages this month as its plan allows before throttling (Docs/Billing.md, Messages). The
/// management host sends the whole list every minute (<c>MessageThrottleSnapshot</c>); a list older than
/// <see cref="StaleAfter"/> is ignored, so a management outage lets messages through rather than
/// throttling an account that has since upgraded. Each client's minute is kept here by its id, not on its
/// session, so a client that reconnects does not get a fresh message.
/// </summary>
public sealed class MessageThrottle(TimeProvider time)
{
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private volatile Snapshot _current = new([], DateTime.MinValue);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastLetThrough = new();

    /// <summary>Takes the list sent at <paramref name="takenAtUtc"/>, unless a later one is already held.</summary>
    public void Replace(IReadOnlyCollection<int> workspaceIds, DateTime takenAtUtc)
    {
        var next = new Snapshot(workspaceIds.ToHashSet(), takenAtUtc);
        Snapshot current;
        do
        {
            current = _current;
            if (current.TakenAtUtc > takenAtUtc)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _current, next, current) != current);

        // A client whose minute is up has nothing left to remember, so the map holds only the clients
        // throttled in the last minute.
        DateTimeOffset now = time.GetUtcNow();
        foreach (KeyValuePair<Guid, DateTimeOffset> entry in _lastLetThrough)
        {
            if (now - entry.Value >= MessageAllowance.ThrottledInterval)
            {
                _lastLetThrough.TryRemove(entry);
            }
        }
    }

    public bool IsThrottled(int workspaceId)
    {
        Snapshot current = _current;
        return time.GetUtcNow().UtcDateTime - current.TakenAtUtc < StaleAfter && current.WorkspaceIds.Contains(workspaceId);
    }

    /// <summary>
    /// Takes one of a client's own messages: true while its workspace is not throttled, and once every
    /// <see cref="MessageAllowance.ThrottledInterval"/> while it is, however many sessions the client opens.
    /// </summary>
    public bool TryTake(int workspaceId, Guid clientRefId)
    {
        if (!IsThrottled(workspaceId))
        {
            return true;
        }

        DateTimeOffset now = time.GetUtcNow();
        while (true)
        {
            if (!_lastLetThrough.TryGetValue(clientRefId, out DateTimeOffset last))
            {
                if (_lastLetThrough.TryAdd(clientRefId, now))
                {
                    return true;
                }

                continue;
            }

            if (now - last < MessageAllowance.ThrottledInterval)
            {
                return false;
            }

            if (_lastLetThrough.TryUpdate(clientRefId, now, last))
            {
                return true;
            }
        }
    }

    private sealed record Snapshot(HashSet<int> WorkspaceIds, DateTime TakenAtUtc);
}

/// <summary>
/// One session's view of its client's allowance in a throttled workspace (<see cref="MessageThrottle.TryTake"/>),
/// and how many of its messages were held back in a row. Used by the session's receive loop only.
/// </summary>
public sealed class ClientAllowance(MessageThrottle throttle, int workspaceId, Guid clientRefId)
{
    /// <summary>Messages held back since the last one let through.</summary>
    public int ConsecutiveHeldBack { get; private set; }

    /// <summary>Takes one of the client's own messages; false when the workspace is throttled and this client already sent one this minute.</summary>
    public bool TryTake()
    {
        if (!throttle.TryTake(workspaceId, clientRefId))
        {
            ConsecutiveHeldBack++;
            return false;
        }

        ConsecutiveHeldBack = 0;
        return true;
    }
}
