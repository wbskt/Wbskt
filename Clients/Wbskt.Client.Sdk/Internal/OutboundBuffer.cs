using Wbskt.Client.Sdk.Models;

namespace Wbskt.Client.Sdk.Internal;

/// <summary>
/// Bounded, in-memory FIFO of messages waiting for a connection. When full, the oldest
/// message is dropped so the most recent readings survive a long outage.
/// </summary>
internal sealed class OutboundBuffer(int capacity)
{
    private readonly LinkedList<SocketMessage> _items = new();
    private readonly object _gate = new();

    public int Capacity { get; } = capacity;

    public int Count
    {
        get { lock (_gate) { return _items.Count; } }
    }

    public long DroppedCount { get; private set; }

    public void Enqueue(SocketMessage message)
    {
        lock (_gate)
        {
            if (Capacity <= 0)
            {
                DroppedCount++;
                return;
            }

            while (_items.Count >= Capacity)
            {
                _items.RemoveFirst();
                DroppedCount++;
            }

            _items.AddLast(message);
        }
    }

    public bool TryPeek(out SocketMessage message)
    {
        lock (_gate)
        {
            if (_items.First is { } first)
            {
                message = first.Value;
                return true;
            }

            message = null!;
            return false;
        }
    }

    /// <summary>
    /// Removes <paramref name="message"/> if it is still at the head. It may already be gone
    /// when the buffer overflowed while it was being sent.
    /// </summary>
    public void RemoveIfHead(SocketMessage message)
    {
        lock (_gate)
        {
            if (_items.First is { } first && ReferenceEquals(first.Value, message))
            {
                _items.RemoveFirst();
            }
        }
    }
}
