namespace Wbskt.Infrastructure;

/// <summary>
/// A half-open time range [From, To) read from a list or chart endpoint's <c>from</c> and <c>to</c>
/// query parameters. Both are <see cref="DateTimeOffset"/> on every endpoint, and the defaults and
/// the longest allowed span are applied here so each endpoint states only its own numbers.
/// </summary>
public readonly record struct TimeRange(DateTime FromUtc, DateTime ToUtc)
{
    /// <summary>
    /// Fills in a missing <paramref name="to"/> with <paramref name="now"/> and a missing
    /// <paramref name="from"/> with <paramref name="defaultSpan"/> before <c>to</c>, then rejects a
    /// range that is empty, starts before 1970, or spans more than <paramref name="maxSpan"/>.
    /// </summary>
    public static Result<TimeRange> Resolve(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now, TimeSpan defaultSpan, TimeSpan maxSpan)
    {
        var toUtc = (to ?? now).UtcDateTime;
        var fromUtc = from?.UtcDateTime ?? toUtc - defaultSpan;

        if (fromUtc < DateTime.UnixEpoch)
        {
            return Result<TimeRange>.Failure(Error.Validation("TIME_RANGE_INVALID", "'from' must be after 1970."));
        }

        if (fromUtc >= toUtc)
        {
            return Result<TimeRange>.Failure(Error.Validation("TIME_RANGE_INVALID", "'from' must be before 'to'."));
        }

        if (toUtc - fromUtc > maxSpan)
        {
            return Result<TimeRange>.Failure(Error.Validation("TIME_RANGE_TOO_LONG", $"A range can span at most {maxSpan.TotalDays:0} days."));
        }

        return Result<TimeRange>.Success(new TimeRange(fromUtc, toUtc));
    }
}
