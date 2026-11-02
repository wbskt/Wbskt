namespace Wbskt.Protocol;

/// <summary>
/// A token bucket: <see cref="Burst"/> tokens to start with, refilled at <see cref="PerSecond"/>.
/// The gateway uses one to drop a device's excess messages; the SDKs use one to pace their own sends
/// below that budget. It refills from elapsed time rather than a timer and is not thread-safe.
/// </summary>
public sealed class TokenBucket
{
    private readonly TimeProvider _time;
    private double _tokens;
    private long _lastRefill;

    public TokenBucket(double perSecond, int burst, TimeProvider? time = null)
    {
        PerSecond = Math.Max(0.1, perSecond);
        Burst = Math.Max(1, burst);
        _time = time ?? TimeProvider.System;
        _tokens = Burst;
        _lastRefill = _time.GetTimestamp();
    }

    public double PerSecond { get; }

    public int Burst { get; }

    /// <summary>Takes one token; false when the bucket is empty.</summary>
    public bool TryTake()
    {
        Refill();
        if (_tokens < 1)
        {
            return false;
        }

        _tokens -= 1;
        return true;
    }

    /// <summary>How long until a token is available; zero when one already is.</summary>
    public TimeSpan TimeUntilNext()
    {
        Refill();
        return _tokens >= 1 ? TimeSpan.Zero : TimeSpan.FromSeconds((1 - _tokens) / PerSecond);
    }

    /// <summary>Waits for a token and takes it.</summary>
    public async Task TakeAsync(CancellationToken cancellationToken = default)
    {
        while (!TryTake())
        {
            await Task.Delay(TimeUntilNext(), _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Refill()
    {
        var now = _time.GetTimestamp();
        _tokens = Math.Min(Burst, _tokens + _time.GetElapsedTime(_lastRefill, now).TotalSeconds * PerSecond);
        _lastRefill = now;
    }
}
