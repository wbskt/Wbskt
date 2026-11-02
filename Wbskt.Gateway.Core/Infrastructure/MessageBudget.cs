using Wbskt.Protocol;

namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>How fast one connected device may send (<c>Gateway:MessageBudget</c>).</summary>
public sealed class MessageBudgetOptions
{
    public const string SectionName = "Gateway:MessageBudget";

    /// <summary>Messages per second a device may sustain.</summary>
    public double PerSecond { get; set; } = 10;

    /// <summary>How many it may send at once after being quiet, such as a buffer flushed on reconnect.</summary>
    public int Burst { get; set; } = 100;

    /// <summary>
    /// Messages dropped in a row before the connection is closed. A device a little over its budget
    /// just loses the excess; one in a tight send loop is cut off.
    /// </summary>
    public int CloseAfterDropped { get; set; } = 500;
}

/// <summary>
/// One connection's inbound budget: a <see cref="TokenBucket"/> plus a count of messages dropped in a
/// row. Used by its receive loop only, so it needs no lock.
/// </summary>
public sealed class MessageBudget
{
    private readonly TokenBucket _bucket;

    public MessageBudget(MessageBudgetOptions options, TimeProvider? time = null)
    {
        _bucket = new TokenBucket(options.PerSecond, options.Burst, time);
        CloseAfterDropped = Math.Max(1, options.CloseAfterDropped);
    }

    public int CloseAfterDropped { get; }

    /// <summary>Messages dropped since the last one let through.</summary>
    public int ConsecutiveDropped { get; private set; }

    /// <summary>Whether the device has exceeded its budget long enough to be disconnected.</summary>
    public bool Exhausted => ConsecutiveDropped >= CloseAfterDropped;

    /// <summary>Takes one message from the budget; false when the device is over it.</summary>
    public bool TryTake()
    {
        if (_bucket.TryTake())
        {
            ConsecutiveDropped = 0;
            return true;
        }

        ConsecutiveDropped++;
        return false;
    }
}
