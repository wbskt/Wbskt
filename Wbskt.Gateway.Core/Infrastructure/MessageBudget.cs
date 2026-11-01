namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>How fast one connected device may send (<c>Socket:MessageBudget</c>).</summary>
public sealed class MessageBudgetOptions
{
    public const string SectionName = "Socket:MessageBudget";

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
/// A token bucket for one connection's inbound messages. Used by its receive loop only, so it needs
/// no lock, and it refills from elapsed time rather than a timer per connection.
/// </summary>
public sealed class MessageBudget
{
    private readonly double _perSecond;
    private readonly double _burst;
    private readonly TimeProvider _time;
    private double _tokens;
    private long _lastRefill;

    public MessageBudget(MessageBudgetOptions options, TimeProvider? time = null)
    {
        _perSecond = Math.Max(0.1, options.PerSecond);
        _burst = Math.Max(1, options.Burst);
        CloseAfterDropped = Math.Max(1, options.CloseAfterDropped);
        _time = time ?? TimeProvider.System;
        _tokens = _burst;
        _lastRefill = _time.GetTimestamp();
    }

    public int CloseAfterDropped { get; }

    /// <summary>Messages dropped since the last one let through.</summary>
    public int ConsecutiveDropped { get; private set; }

    /// <summary>Whether the device has exceeded its budget long enough to be disconnected.</summary>
    public bool Exhausted => ConsecutiveDropped >= CloseAfterDropped;

    /// <summary>Takes one message from the budget; false when the device is over it.</summary>
    public bool TryTake()
    {
        var now = _time.GetTimestamp();
        _tokens = Math.Min(_burst, _tokens + _time.GetElapsedTime(_lastRefill, now).TotalSeconds * _perSecond);
        _lastRefill = now;

        if (_tokens >= 1)
        {
            _tokens -= 1;
            ConsecutiveDropped = 0;
            return true;
        }

        ConsecutiveDropped++;
        return false;
    }
}
