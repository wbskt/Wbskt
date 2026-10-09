using System.Globalization;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services.Readings;

public interface IClientReadingService
{
    /// <summary>
    /// One variable's history over [from, to), summarised per bucket. <paramref name="bucket"/> is
    /// "30s", "5m", "1h", "1d" or a number of seconds; without one, a size is picked that gives at
    /// most <see cref="ClientReadingService.TargetBuckets"/> buckets. The range defaults to the last day.
    /// </summary>
    Task<Result<ClientReadingsResponse>> GetBucketsAsync(int clientId, string? name, DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken cancellationToken = default);

    /// <summary>The raw readings over [from, to), for export: one variable, or all of them when <paramref name="name"/> is null.</summary>
    Task<Result<IReadOnlyCollection<ClientReading>>> GetRangeAsync(int clientId, string? name, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
}

public sealed class ClientReadingService : IClientReadingService
{
    internal static readonly TimeSpan DefaultRange = TimeSpan.FromDays(1);
    internal static readonly TimeSpan MaxRange = TimeSpan.FromDays(400);
    internal const int TargetBuckets = 500;
    internal const int MaxBuckets = 2000;
    internal const int MaxExportRows = 100_000;
    private const int MaxNameLength = 100;

    // Sizes picked when the caller names none: round numbers a chart's axis reads well in.
    private static readonly int[] BucketLadder =
        [1, 10, 30, 60, 300, 900, 1800, 3600, 3 * 3600, 6 * 3600, 12 * 3600, 86400, 7 * 86400];

    private readonly IClientReadingProvider _provider;
    private readonly TimeProvider _timeProvider;

    public ClientReadingService(IClientReadingProvider provider) : this(provider, TimeProvider.System)
    {
    }

    internal ClientReadingService(IClientReadingProvider provider, TimeProvider timeProvider)
    {
        _provider = provider;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ClientReadingsResponse>> GetBucketsAsync(int clientId, string? name, DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
        {
            return Result<ClientReadingsResponse>.Failure(Error.Validation("READINGS_NAME_REQUIRED", "Name the state variable to read, in 'name'."));
        }

        var range = ResolveRange(from, to);
        if (range.IsFailure)
        {
            return Result<ClientReadingsResponse>.Failure(range.Error);
        }

        var (fromUtc, toUtc) = range.Value;

        int bucketSeconds;
        if (bucket is null)
        {
            bucketSeconds = PickBucket(toUtc - fromUtc);
        }
        else if (!TryParseBucket(bucket, out bucketSeconds))
        {
            return Result<ClientReadingsResponse>.Failure(Error.Validation("READINGS_BUCKET_INVALID", "'bucket' must be a number of seconds, or a number followed by s, m, h or d, such as 5m."));
        }

        // Buckets line up on whole multiples of their size, so the same reading lands in the same
        // bucket whatever range was asked for, and an hourly bucket starts on the hour.
        fromUtc = AlignDown(fromUtc, bucketSeconds);

        if (Math.Ceiling((toUtc - fromUtc).TotalSeconds / bucketSeconds) > MaxBuckets)
        {
            return Result<ClientReadingsResponse>.Failure(Error.Validation("READINGS_TOO_MANY_BUCKETS", $"That range and bucket size make more than {MaxBuckets} buckets; use a larger bucket or a shorter range."));
        }

        var buckets = await _provider.GetBucketsAsync(clientId, name, fromUtc, toUtc, bucketSeconds, cancellationToken);
        return Result<ClientReadingsResponse>.Success(new ClientReadingsResponse(name, fromUtc, toUtc, bucketSeconds, buckets));
    }

    public async Task<Result<IReadOnlyCollection<ClientReading>>> GetRangeAsync(int clientId, string? name, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
    {
        if (name is { Length: 0 or > MaxNameLength })
        {
            return Result<IReadOnlyCollection<ClientReading>>.Failure(Error.Validation("READINGS_NAME_INVALID", "'name' must be a state variable name, or left out for every variable."));
        }

        var range = ResolveRange(from, to);
        if (range.IsFailure)
        {
            return Result<IReadOnlyCollection<ClientReading>>.Failure(range.Error);
        }

        // One more than is returned, to tell a range that fits from one that was cut off.
        var readings = await _provider.GetRangeAsync(clientId, name, range.Value.FromUtc, range.Value.ToUtc, MaxExportRows + 1, cancellationToken);
        if (readings.Count > MaxExportRows)
        {
            return Result<IReadOnlyCollection<ClientReading>>.Failure(Error.Validation("READINGS_RANGE_TOO_LARGE", $"That range holds more than {MaxExportRows} readings; export a shorter range or a single variable."));
        }

        return Result<IReadOnlyCollection<ClientReading>>.Success(readings);
    }

    internal static bool TryParseBucket(string text, out int seconds)
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
        if (total < 1 || total > MaxRange.TotalSeconds)
        {
            return false;
        }

        seconds = (int)total;
        return true;
    }

    internal static int PickBucket(TimeSpan range)
    {
        foreach (var size in BucketLadder)
        {
            if (range.TotalSeconds / size <= TargetBuckets)
            {
                return size;
            }
        }

        return BucketLadder[^1];
    }

    private static DateTime AlignDown(DateTime utc, int bucketSeconds)
    {
        var size = TimeSpan.FromSeconds(bucketSeconds).Ticks;
        var sinceEpoch = utc.Ticks - DateTime.UnixEpoch.Ticks;
        return new DateTime(DateTime.UnixEpoch.Ticks + sinceEpoch / size * size, DateTimeKind.Utc);
    }

    private Result<TimeRange> ResolveRange(DateTimeOffset? from, DateTimeOffset? to)
        => TimeRange.Resolve(from, to, _timeProvider.GetUtcNow(), DefaultRange, MaxRange);
}
