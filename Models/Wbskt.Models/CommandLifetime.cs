namespace Wbskt.Models;

/// <summary>
/// How long a command to a client may live, whoever sends it: the console, the API or a workflow
/// step. The client command sender is the one place it is held: it refuses an expiry that has already
/// passed, which nothing would ever deliver, and one further ahead than <see cref="Max"/>.
/// </summary>
public static class CommandLifetime
{
    public static readonly TimeSpan Max = TimeSpan.FromHours(24);

    /// <summary>Whether <paramref name="expiresAt"/> is after <paramref name="now"/> and at most <see cref="Max"/> after it.</summary>
    public static bool IsValidExpiry(DateTimeOffset expiresAt, DateTimeOffset now) => expiresAt > now && expiresAt <= now + Max;
}
