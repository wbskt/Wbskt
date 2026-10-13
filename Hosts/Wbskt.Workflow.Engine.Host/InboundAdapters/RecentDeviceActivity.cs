using System.Collections.Concurrent;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// The devices this engine has heard connect or send in the last few seconds. Such a device is
/// connected even when its presence row still reads offline: the management host records the
/// connection from the socket host's event, and that write can land after a run the device's own
/// message started (#141). <see cref="DeviceCommandPublisher"/> re-checks an offline device only
/// when it is here, so commands to devices that are simply offline fail at once.
/// </summary>
/// <remarks>
/// Held in memory: only the leader engine consumes the bus and runs branches, so the instance that
/// hears a device is the one that commands it.
/// </remarks>
public sealed class RecentDeviceActivity(TimeProvider clock)
{
    /// <summary>Well past the few hundred milliseconds presence takes to land.</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _seen = new();
    private long _nextSweepTicks;

    public void Saw(Guid clientRefId)
    {
        DateTimeOffset now = clock.GetUtcNow();
        _seen[clientRefId] = now;

        // Forget devices gone quiet, at most once a window, so the map holds only recent ones.
        long next = Interlocked.Read(ref _nextSweepTicks);
        if (now.UtcTicks >= next && Interlocked.CompareExchange(ref _nextSweepTicks, (now + Window).UtcTicks, next) == next)
        {
            foreach (var (refId, at) in _seen)
            {
                if (now - at > Window)
                {
                    _seen.TryRemove(new KeyValuePair<Guid, DateTimeOffset>(refId, at));
                }
            }
        }
    }

    public bool WasRecentlyActive(Guid clientRefId) =>
        _seen.TryGetValue(clientRefId, out DateTimeOffset at) && clock.GetUtcNow() - at <= Window;

    internal int Count => _seen.Count;
}
