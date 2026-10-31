using System.Collections.Concurrent;

namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// Caps the client-scoped live feed at <see cref="MaxPerSecond"/> messages per device per second, so a
/// device page watching a chatty device stays readable and the backplane isn't flooded. Counted per
/// management instance: the forwarder queues are competing consumers, so with one instance (the
/// hobbyist VM) the cap is exact and with more it is a per-instance bound.
/// </summary>
public sealed class ClientFeedThrottle
{
    internal const int MaxPerSecond = 20;

    private readonly ConcurrentDictionary<int, Window> _windows = new();
    private readonly TimeProvider _time;

    public ClientFeedThrottle(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Whether one more message for <paramref name="clientId"/> may go out now. <paramref name="skipped"/>
    /// is how many were dropped in the window that just closed, reported once, on the first call of the
    /// next window, so the page can say how many it missed.
    /// </summary>
    public bool TryAcquire(int clientId, out int skipped)
    {
        var second = _time.GetUtcNow().ToUnixTimeSeconds();
        var window = _windows.GetOrAdd(clientId, _ => new Window());

        lock (window)
        {
            skipped = 0;
            if (window.Second != second)
            {
                skipped = window.Skipped;
                window.Second = second;
                window.Sent = 0;
                window.Skipped = 0;
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

    private sealed class Window
    {
        public long Second = long.MinValue;
        public int Sent;
        public int Skipped;
    }
}
