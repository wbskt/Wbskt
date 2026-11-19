using System.Globalization;

namespace Wbskt.Infrastructure;

/// <summary>
/// The <c>bucket</c> parameter every chart endpoint takes, beside <see cref="TimeRange"/>: a number of
/// seconds, or a number followed by s, m, h or d (<c>90</c>, <c>5m</c>, <c>1h</c>, <c>1d</c>). Buckets line up
/// on whole multiples of their size since 1970, so the same moment lands in the same bucket whatever range
/// was asked for, and an hourly bucket starts on the hour.
/// </summary>
public static class ChartBuckets
{
    /// <summary>The size in seconds, or false for text that is not one, or not between a second and <paramref name="max"/>.</summary>
    public static bool TryParse(string text, TimeSpan max, out int seconds)
    {
        seconds = 0;
        text = text.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var unit = char.ToLowerInvariant(text[^1]);
        var multiplier = unit switch { 's' => 1, 'm' => 60, 'h' => 3600, 'd' => 86400, _ => 0 };
        var digits = multiplier == 0 ? text : text[..^1];
        multiplier = multiplier == 0 ? 1 : multiplier;

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return false;
        }

        var total = count * multiplier;
        if (total < 1 || total > max.TotalSeconds)
        {
            return false;
        }

        seconds = (int)total;
        return true;
    }

    /// <summary>The start of the bucket <paramref name="utc"/> falls in.</summary>
    public static DateTime AlignDown(DateTime utc, int bucketSeconds)
    {
        var size = TimeSpan.FromSeconds(bucketSeconds).Ticks;
        var sinceEpoch = utc.Ticks - DateTime.UnixEpoch.Ticks;
        return new DateTime(DateTime.UnixEpoch.Ticks + sinceEpoch / size * size, DateTimeKind.Utc);
    }
}
