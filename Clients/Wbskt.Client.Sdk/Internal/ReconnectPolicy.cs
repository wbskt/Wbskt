namespace Wbskt.Client.Sdk.Internal;

/// <summary>
/// When to try again after the connection drops. Every delay is drawn at random from zero up to a
/// ceiling ("full jitter"), so a fleet that lost its socket host at the same moment, on a deploy,
/// comes back spread out instead of all logging in within the same second.
/// </summary>
internal sealed class ReconnectPolicy
{
    /// <summary>The first attempt after an ordinary drop lands somewhere in this window.</summary>
    internal static readonly TimeSpan Spread = TimeSpan.FromSeconds(10);

    /// <summary>The window when the server closed with <c>EndpointUnavailable</c>: it is restarting.</summary>
    internal static readonly TimeSpan RestartSpread = TimeSpan.FromSeconds(30);

    internal static readonly TimeSpan Base = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan Cap = TimeSpan.FromMinutes(1);

    private readonly Random _random;

    public ReconnectPolicy(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public TimeSpan FirstDelay(bool serverRestarting) => UpTo(serverRestarting ? RestartSpread : Spread);

    /// <summary>The wait after <paramref name="failures"/> failed attempts in a row: up to base × 2^failures, capped.</summary>
    public TimeSpan RetryDelay(int failures)
    {
        // Shift bounded so it can't overflow; 2 s << 5 is already past the cap.
        long ceiling = Math.Min(Cap.Ticks, Base.Ticks << Math.Clamp(failures, 0, 10));
        return UpTo(TimeSpan.FromTicks(ceiling));
    }

    /// <summary>The wait after a 429: what the server asked for, plus jitter so those told the same don't return together.</summary>
    public TimeSpan AfterThrottle(TimeSpan retryAfter) => retryAfter + UpTo(Base);

    private TimeSpan UpTo(TimeSpan max) => TimeSpan.FromTicks((long)(_random.NextDouble() * max.Ticks));
}
