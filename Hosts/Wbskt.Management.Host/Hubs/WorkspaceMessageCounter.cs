using System.Collections.Concurrent;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// Counts client traffic per workspace so the console can show a workspace-wide message rate without
/// joining every client's feed. Fed by <c>SignalRForwardingHandler</c> before the per-client cap, so
/// messages <see cref="ClientFeedThrottle"/> drops still count. Like the throttle it counts per
/// management instance: the forwarder queues are competing consumers, so with one instance (the
/// hobbyist VM) the rate is the workspace's and with more each instance reports its share.
/// Events older than <see cref="MaxAge"/> are not counted: after a restart or a slow spell the
/// forwarder queue drains in seconds, and counting that backlog would read as a burst of traffic
/// that happened minutes ago.
/// </summary>
public sealed class WorkspaceMessageCounter
{
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan MaxAge = Window * 2;

    private readonly ConcurrentDictionary<int, Tally> _tallies = new();
    private readonly TimeProvider _time;
    private DateTimeOffset _lastFlush;

    public WorkspaceMessageCounter(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _lastFlush = _time.GetUtcNow();
    }

    /// <summary>Counts <paramref name="message"/> if it is recent client traffic; any other event is ignored.</summary>
    public void Count(object message)
    {
        if (message is IEvent { CreatedAtUtc: var createdAtUtc } && createdAtUtc < _time.GetUtcNow().UtcDateTime - MaxAge)
        {
            return;
        }

        switch (message)
        {
            case ClientMessageReceivedEvent received:
                Interlocked.Increment(ref Tallied(received.WorkspaceId).Inbound);
                break;
            case ClientCommandDeliveredEvent delivered:
                Interlocked.Increment(ref Tallied(delivered.WorkspaceId).Outbound);
                break;
        }
    }

    /// <summary>
    /// Closes the window: returns the rate for every workspace that had traffic in it, plus a zero for
    /// each that had traffic in the window before and none in this one, so the console sees it go quiet.
    /// </summary>
    public IReadOnlyList<(int WorkspaceId, WorkspaceMessageRate Rate)> Flush()
    {
        var now = _time.GetUtcNow();
        // The timer can run late; dividing by the real elapsed time keeps a slow tick from reading as a spike.
        var seconds = Math.Max(1, (now - _lastFlush).TotalSeconds);
        _lastFlush = now;

        var rates = new List<(int, WorkspaceMessageRate)>();
        foreach (var (workspaceId, tally) in _tallies)
        {
            var inbound = Interlocked.Exchange(ref tally.Inbound, 0);
            var outbound = Interlocked.Exchange(ref tally.Outbound, 0);
            var rate = new WorkspaceMessageRate(
                PerSecond(inbound, seconds),
                PerSecond(outbound, seconds),
                PerSecond(inbound + outbound, seconds),
                (int)Math.Round(seconds),
                now);

            var busy = inbound + outbound > 0;
            if (busy || tally.WasBusy)
            {
                rates.Add((workspaceId, rate));
            }

            tally.WasBusy = busy;
            tally.Latest = rate;
        }

        return rates;
    }

    /// <summary>The rate from the last closed window, or zero when the workspace has had no traffic.</summary>
    public WorkspaceMessageRate Current(int workspaceId)
    {
        return _tallies.TryGetValue(workspaceId, out var tally)
            ? tally.Latest
            : Quiet();
    }

    private Tally Tallied(int workspaceId)
    {
        return _tallies.GetOrAdd(workspaceId, _ => new Tally { Latest = Quiet() });
    }

    private WorkspaceMessageRate Quiet()
    {
        return new WorkspaceMessageRate(0, 0, 0, (int)Window.TotalSeconds, _time.GetUtcNow());
    }

    private static double PerSecond(long count, double seconds)
    {
        return Math.Round(count / seconds, 1);
    }

    private sealed class Tally
    {
        public long Inbound;
        public long Outbound;
        public bool WasBusy;
        public required WorkspaceMessageRate Latest { get; set; }
    }
}
