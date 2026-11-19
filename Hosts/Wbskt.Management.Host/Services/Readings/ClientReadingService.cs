using System.Globalization;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Csv;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services.Readings;

public interface IClientReadingService
{
    /// <summary>
    /// A client of another workspace, or none, is <c>CLIENT_NOT_FOUND</c> in both methods.
    /// One variable's history over [from, to), summarised per bucket. <paramref name="bucket"/> is
    /// "30s", "5m", "1h", "1d" or a number of seconds; without one, a size is picked that gives at
    /// most <see cref="ClientReadingService.TargetBuckets"/> buckets. The range defaults to the last day.
    /// </summary>
    Task<Result<ClientReadingsResponse>> GetBucketsAsync(int workspaceId, Guid clientRef, string? name, DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// The readings over [from, to) as CSV, for one variable or all of them when <paramref name="name"/>
    /// is null. <paramref name="resolution"/> "raw" (the default) gives every reading, which reaches
    /// back only the plan's raw days; "hourly" gives one row per variable and hour, raw and rolled-up
    /// alike, as far back as the chart.
    /// </summary>
    Task<Result<CsvExport>> ExportCsvAsync(int workspaceId, Guid clientRef, string? name, DateTimeOffset? from, DateTimeOffset? to, string? resolution, CancellationToken cancellationToken = default);
}

public sealed class ClientReadingService : IClientReadingService
{
    internal static readonly TimeSpan DefaultRange = TimeSpan.FromDays(1);
    // Two years and a little: the longest a plan keeps the hourly roll-up (Pro's HourlyReadingsDays).
    internal static readonly TimeSpan MaxRange = TimeSpan.FromDays(740);
    internal const int TargetBuckets = 500;
    internal const int MaxBuckets = 2000;
    internal const int MaxExportRows = 100_000;
    private const int MaxNameLength = 100;
    private static readonly Error NameInvalid = Error.Validation("READINGS_NAME_INVALID", "'name' must be a state variable name, or left out for every variable.");
    private static readonly Error RangeTooLarge = Error.Validation("READINGS_RANGE_TOO_LARGE", $"That range holds more than {MaxExportRows} rows; export a shorter range, a single variable or resolution=hourly.");

    // Sizes picked when the caller names none: round numbers a chart's axis reads well in.
    private static readonly int[] BucketLadder =
        [1, 10, 30, 60, 300, 900, 1800, 3600, 3 * 3600, 6 * 3600, 12 * 3600, 86400, 7 * 86400];

    private readonly IClientReadingProvider _provider;
    private readonly IClientQueryService _clients;
    private readonly TimeProvider _timeProvider;

    public ClientReadingService(IClientReadingProvider provider, IClientQueryService clients, TimeProvider timeProvider)
    {
        _provider = provider;
        _clients = clients;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ClientReadingsResponse>> GetBucketsAsync(int workspaceId, Guid clientRef, string? name, DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken cancellationToken = default)
    {
        var client = await _clients.EnsureClientInWorkspaceAsync(workspaceId, clientRef, cancellationToken);
        if (client.IsFailure)
        {
            return Result<ClientReadingsResponse>.Failure(client.Error);
        }

        var clientId = client.Value;
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
        return Result<ClientReadingsResponse>.Success(new ClientReadingsResponse(name, fromUtc.ToUtcOffset(), toUtc.ToUtcOffset(), bucketSeconds, buckets));
    }

    public async Task<Result<CsvExport>> ExportCsvAsync(int workspaceId, Guid clientRef, string? name, DateTimeOffset? from, DateTimeOffset? to, string? resolution, CancellationToken cancellationToken = default)
    {
        // A range over the limit is refused before anything is written (READINGS_RANGE_TOO_LARGE), so
        // these rows are already in hand; the file is still written a row at a time.
        switch (resolution?.ToLowerInvariant())
        {
            case null or "raw":
                var readings = await GetRangeAsync(workspaceId, clientRef, name, from, to, cancellationToken);
                return readings.IsSuccess
                    ? Result<CsvExport>.Success(new CsvExport(ClientReadingsCsv.Header, readings.Value.Select(ClientReadingsCsv.Row).ToAsyncEnumerable(), MaxExportRows))
                    : Result<CsvExport>.Failure(readings.Error);
            case "hourly":
                var hours = await GetHourlyRangeAsync(workspaceId, clientRef, name, from, to, cancellationToken);
                return hours.IsSuccess
                    ? Result<CsvExport>.Success(new CsvExport(ClientReadingsCsv.HourlyHeader, hours.Value.Select(ClientReadingsCsv.HourlyRow).ToAsyncEnumerable(), MaxExportRows))
                    : Result<CsvExport>.Failure(hours.Error);
            default:
                return Result<CsvExport>.Failure(Error.Validation("READINGS_RESOLUTION_INVALID", "'resolution' must be 'raw' or 'hourly'."));
        }
    }

    internal async Task<Result<IReadOnlyCollection<ClientReadingHour>>> GetHourlyRangeAsync(int workspaceId, Guid clientRef, string? name, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
    {
        var client = await _clients.EnsureClientInWorkspaceAsync(workspaceId, clientRef, cancellationToken);
        if (client.IsFailure)
        {
            return Result<IReadOnlyCollection<ClientReadingHour>>.Failure(client.Error);
        }

        if (name is { Length: 0 or > MaxNameLength })
        {
            return Result<IReadOnlyCollection<ClientReadingHour>>.Failure(NameInvalid);
        }

        var range = ResolveRange(from, to);
        if (range.IsFailure)
        {
            return Result<IReadOnlyCollection<ClientReadingHour>>.Failure(range.Error);
        }

        var hours = await _provider.GetHourlyRangeAsync(client.Value, name, range.Value.FromUtc, range.Value.ToUtc, MaxExportRows + 1, cancellationToken);
        if (hours.Count > MaxExportRows)
        {
            return Result<IReadOnlyCollection<ClientReadingHour>>.Failure(RangeTooLarge);
        }

        return Result<IReadOnlyCollection<ClientReadingHour>>.Success(hours);
    }

    internal async Task<Result<IReadOnlyCollection<ClientReading>>> GetRangeAsync(int workspaceId, Guid clientRef, string? name, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
    {
        var client = await _clients.EnsureClientInWorkspaceAsync(workspaceId, clientRef, cancellationToken);
        if (client.IsFailure)
        {
            return Result<IReadOnlyCollection<ClientReading>>.Failure(client.Error);
        }

        var clientId = client.Value;
        if (name is { Length: 0 or > MaxNameLength })
        {
            return Result<IReadOnlyCollection<ClientReading>>.Failure(NameInvalid);
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
            return Result<IReadOnlyCollection<ClientReading>>.Failure(RangeTooLarge);
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
