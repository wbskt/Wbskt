using System.Collections.Concurrent;

namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// Caps the client-scoped live feed at <see cref="MaxPerSecond"/> messages per client per second, so a
/// client page watching a chatty client stays readable and the backplane isn't flooded. Counted per
/// management instance: the forwarder queues are competing consumers, so with one instance (the
/// hobbyist VM) the cap is exact and with more it is a per-instance bound.
/// </summary>
/// <remarks>
/// What it drops is reported by <see cref="ClientFeedSkipReporter"/> once a second, from
/// <see cref="TakeSkipped"/>, whether or not the client sends again; a client quiet for
/// <see cref="IdleAfter"/> is forgotten there too, so the table holds only clients sending lately.
/// </remarks>
public sealed class ClientFeedThrottle
{
    internal const int MaxPerSecond = 20;

    internal static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<int, Window> _windows = new();
    private readonly TimeProvider _time;

    public ClientFeedThrottle(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    internal int Count => _windows.Count;

    /// <summary>Whether one more message for <paramref name="clientId"/> may go out now; a refused one is counted to be reported.</summary>
    public bool TryAcquire(int clientId, Guid clientRefId)
    {
        var second = _time.GetUtcNow().ToUnixTimeSeconds();
        while (true)
        {
            var window = _windows.GetOrAdd(clientId, _ => new Window(clientRefId));
            lock (window)
            {
                // Forgotten between the lookup and the lock: take the fresh one.
                if (window.Forgotten)
                {
                    continue;
                }

                if (window.Second != second)
                {
                    window.Second = second;
                    window.Sent = 0;
                }

                if (window.Sent < MaxPerSecond)
                {
                    window.Sent++;
                    return true;
                }

                window.Skipped++;
                return false;
            }
        }
    }

    /// <summary>
    /// The messages dropped per client since the last call, stamped with this throttle's clock; and
    /// forgets each client that has sent nothing for <see cref="IdleAfter"/> and has nothing to report.
    /// </summary>
    public IReadOnlyList<(int ClientId, ClientFeedSkipped Skipped)> TakeSkipped()
    {
        var now = _time.GetUtcNow();
        var idleBefore = (now - IdleAfter).ToUnixTimeSeconds();
        var skipped = new List<(int, ClientFeedSkipped)>();
        foreach (var (clientId, window) in _windows)
        {
            lock (window)
            {
                if (window.Skipped > 0)
                {
                    skipped.Add((clientId, new ClientFeedSkipped(window.ClientRefId, window.Skipped, now.UtcDateTime)));
                    window.Skipped = 0;
                }
                else if (window.Second < idleBefore)
                {
                    window.Forgotten = true;
                    _windows.TryRemove(KeyValuePair.Create(clientId, window));
                }
            }
        }

        return skipped;
    }

    private sealed class Window(Guid clientRefId)
    {
        public readonly Guid ClientRefId = clientRefId;
        public long Second = long.MinValue;
        public int Sent;
        public int Skipped;
        public bool Forgotten;
    }
}
