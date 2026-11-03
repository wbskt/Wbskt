using System.Collections.Concurrent;

namespace Wbskt.Gateway.Core.Services;

/// <summary>
/// Counts, per device, how often a newer connection replaced an older one. One device reconnecting
/// supersedes itself now and then; two devices sharing credentials do it every few seconds, forever.
/// </summary>
public sealed class SupersedeMonitor
{
    internal const int Threshold = 5;
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, DeviceSupersedes> _devices = new();
    private readonly TimeProvider _time;
    private long _records;

    public SupersedeMonitor(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Records one supersede. Returns the count in the window when it reaches <see cref="Threshold"/>
    /// and the device has not been reported within the window; otherwise null.
    /// </summary>
    public int? Record(Guid clientRefId)
    {
        var now = _time.GetUtcNow();
        if (Interlocked.Increment(ref _records) % 256 == 0)
        {
            Prune(now);
        }

        var device = _devices.GetOrAdd(clientRefId, _ => new DeviceSupersedes());
        lock (device)
        {
            while (device.Times.Count > 0 && now - device.Times.Peek() > Window)
            {
                device.Times.Dequeue();
            }

            device.Times.Enqueue(now);
            if (device.Times.Count < Threshold || (device.ReportedAt is { } reported && now - reported < Window))
            {
                return null;
            }

            device.ReportedAt = now;
            return device.Times.Count;
        }
    }

    // Drops devices with nothing in the window, so the map holds only devices that are flapping.
    private void Prune(DateTimeOffset now)
    {
        foreach (var (clientRefId, device) in _devices)
        {
            lock (device)
            {
                if (device.Times.Count == 0 || now - device.Times.Last() > Window && (device.ReportedAt is null || now - device.ReportedAt > Window))
                {
                    _devices.TryRemove(clientRefId, out _);
                }
            }
        }
    }

    private sealed class DeviceSupersedes
    {
        public Queue<DateTimeOffset> Times { get; } = new();
        public DateTimeOffset? ReportedAt { get; set; }
    }
}
