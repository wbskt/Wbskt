using System.Collections.Concurrent;

namespace Wbskt.Gateway.Core.Services;

/// <summary>
/// Counts, per client, how often a newer connection replaced an older one. One client reconnecting
/// supersedes itself now and then; two clients sharing credentials do it every few seconds, forever.
/// </summary>
public sealed class SupersedeMonitor
{
    internal const int Threshold = 5;
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, ClientSupersedes> _clients = new();
    private readonly TimeProvider _time;
    private long _records;

    public SupersedeMonitor(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Records one supersede. Returns the count in the window when it reaches <see cref="Threshold"/>
    /// and the client has not been reported within the window; otherwise null.
    /// </summary>
    public int? Record(Guid clientRefId)
    {
        var now = _time.GetUtcNow();
        if (Interlocked.Increment(ref _records) % 256 == 0)
        {
            Prune(now);
        }

        var client = _clients.GetOrAdd(clientRefId, _ => new ClientSupersedes());
        lock (client)
        {
            while (client.Times.Count > 0 && now - client.Times.Peek() > Window)
            {
                client.Times.Dequeue();
            }

            client.Times.Enqueue(now);
            if (client.Times.Count < Threshold || (client.ReportedAt is { } reported && now - reported < Window))
            {
                return null;
            }

            client.ReportedAt = now;
            return client.Times.Count;
        }
    }

    // Drops clients with nothing in the window, so the map holds only clients that are flapping.
    private void Prune(DateTimeOffset now)
    {
        foreach (var (clientRefId, client) in _clients)
        {
            lock (client)
            {
                if (client.Times.Count == 0 || now - client.Times.Last() > Window && (client.ReportedAt is null || now - client.ReportedAt > Window))
                {
                    _clients.TryRemove(clientRefId, out _);
                }
            }
        }
    }

    private sealed class ClientSupersedes
    {
        public Queue<DateTimeOffset> Times { get; } = new();
        public DateTimeOffset? ReportedAt { get; set; }
    }
}
