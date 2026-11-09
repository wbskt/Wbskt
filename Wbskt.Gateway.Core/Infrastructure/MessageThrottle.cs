using Wbskt.Infrastructure.Billing;

namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>
/// The workspaces whose clients are held to one message a minute, because their account is on Free and
/// has used its monthly messages (Docs/Billing.md, Messages). The management host sends the whole list
/// every minute (<c>MessageThrottleSnapshot</c>); a list older than <see cref="StaleAfter"/> is ignored,
/// so a management outage lets messages through rather than throttling an account that has since paid.
/// </summary>
public sealed class MessageThrottle(TimeProvider time)
{
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private volatile Snapshot _current = new([], DateTime.MinValue);

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
    }

    public bool IsThrottled(int workspaceId)
    {
        Snapshot current = _current;
        return time.GetUtcNow().UtcDateTime - current.TakenAtUtc < StaleAfter && current.WorkspaceIds.Contains(workspaceId);
    }

    private sealed record Snapshot(HashSet<int> WorkspaceIds, DateTime TakenAtUtc);
}

/// <summary>
/// One session's share of a throttled workspace's allowance: a message every
/// <see cref="MessageAllowance.ThrottledInterval"/>, and every message while the workspace is not
/// throttled. Used by the session's receive loop only, so it needs no lock.
/// </summary>
public sealed class ClientAllowance(MessageThrottle throttle, int workspaceId, TimeProvider time)
{
    private DateTimeOffset? _lastLetThrough;

    /// <summary>Messages held back since the last one let through.</summary>
    public int ConsecutiveHeldBack { get; private set; }

    /// <summary>Takes one of the client's own messages; false when the workspace is throttled and this client already sent one this minute.</summary>
    public bool TryTake()
    {
        if (!throttle.IsThrottled(workspaceId))
        {
            ConsecutiveHeldBack = 0;
            return true;
        }

        DateTimeOffset now = time.GetUtcNow();
        if (_lastLetThrough is { } last && now - last < MessageAllowance.ThrottledInterval)
        {
            ConsecutiveHeldBack++;
            return false;
        }

        _lastLetThrough = now;
        ConsecutiveHeldBack = 0;
        return true;
    }
}
