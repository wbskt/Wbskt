using Microsoft.Extensions.Options;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Stats;

/// <summary>
/// Message counts for the Overview chart, from the per-hour and per-day ledgers dbo.ClientTraffic_InsertBatch
/// keeps, so a chart reads at most a few hundred rows however much traffic the range held. Takes the
/// <c>bucket</c> grammar of every chart endpoint (<see cref="ChartBuckets"/>), and the ledgers hold only
/// hours and days. A client's counts (for the client page) come from its own hourly ledger, kept as long as
/// the workspace's hours, and a day there is the sum of its hours.
/// </summary>
internal sealed class WorkspaceMessageHistoryService(IWorkspaceUsageProvider usage, IClientQueryService clients, TimeProvider clock, IOptions<EventLoggingOptions> retention) : IWorkspaceMessageHistoryService
{
    private const int HourSeconds = 3600;
    private const int DaySeconds = 86400;

    internal const string Hour = "1h";
    internal const string Day = "1d";

    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(1);

    // Hourly counts are kept as long as the audit log (EventLogging:RetentionDays); a month of hours is 744 buckets.
    private static readonly TimeSpan MaxHourlyRange = TimeSpan.FromDays(31);
    private static readonly TimeSpan MaxDailyRange = TimeSpan.FromDays(366);

    // Up to this span, a bucket left out means hours.
    private static readonly TimeSpan HourlyUpTo = TimeSpan.FromDays(2);

    public Task<Result<WorkspaceMessageHistory>> GetAsync(int workspaceId, DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken ct) =>
        GetAsync(
            from,
            to,
            bucket,
            daysOutliveHours: true,
            (fromUtc, toUtc, daily) => daily
                ? usage.GetDailyMessagesAsync(workspaceId, DateOnly.FromDateTime(fromUtc), DateOnly.FromDateTime(toUtc.AddDays(-1)), ct)
                : usage.GetHourlyMessagesAsync(workspaceId, fromUtc, toUtc, ct));

    public async Task<Result<WorkspaceMessageHistory>> GetForClientAsync(int workspaceId, Guid clientRef, DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken ct)
    {
        Result<int> client = await clients.EnsureClientInWorkspaceAsync(workspaceId, clientRef, ct);
        if (client.IsFailure)
        {
            return Result<WorkspaceMessageHistory>.Failure(client.Error);
        }

        return await GetAsync(
            from,
            to,
            bucket,
            daysOutliveHours: false,
            (fromUtc, toUtc, daily) => usage.GetClientMessagesAsync(client.Value, fromUtc, toUtc, daily, ct));
    }

    private async Task<Result<WorkspaceMessageHistory>> GetAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? bucket,
        bool daysOutliveHours,
        Func<DateTime, DateTime, bool, Task<IReadOnlyCollection<MessageCountBucket>>> countAsync)
    {
        int? asked = null;
        if (bucket is not null)
        {
            if (!ChartBuckets.TryParse(bucket, MaxDailyRange, out int seconds) || (seconds != HourSeconds && seconds != DaySeconds))
            {
                return Result<WorkspaceMessageHistory>.Failure(Error.Validation("MESSAGES_BUCKET_INVALID", "'bucket' must be an hour or a day: 1h or 1d (3600 or 86400)."));
            }

            asked = seconds;
        }

        DateTimeOffset now = clock.GetUtcNow();
        Result<TimeRange> requested = TimeRange.Resolve(from, to, now, DefaultRange, MaxDailyRange);
        if (requested.IsFailure)
        {
            return Result<WorkspaceMessageHistory>.Failure(requested.Error);
        }

        // Hours older than the hourly ledger's retention are gone; a range reaching back past it is
        // counted by day when no bucket was asked for, and refused when hours were, rather than
        // answering zeros for hours that held traffic. A client's days are its hours, so they go too.
        DateTime hoursKeptFrom = ChartBuckets.AlignDown(now.UtcDateTime - TimeSpan.FromDays(retention.Value.RetentionDays), HourSeconds) + TimeSpan.FromHours(1);
        bool hoursKept = requested.Value.FromUtc >= hoursKeptFrom;
        bool hourly = asked is { } size ? size == HourSeconds : hoursKept && requested.Value.ToUtc - requested.Value.FromUtc <= HourlyUpTo;
        if (!hoursKept && (hourly || !daysOutliveHours))
        {
            return Result<WorkspaceMessageHistory>.Failure(daysOutliveHours
                ? Error.Validation("MESSAGES_HOURLY_EXPIRED", $"Hourly counts are kept for {retention.Value.RetentionDays} days; ask for days (bucket=1d) further back.")
                : Error.Validation("MESSAGES_HOURLY_EXPIRED", $"A client's message counts are kept for {retention.Value.RetentionDays} days; ask for a range inside them."));
        }

        int bucketSeconds = hourly ? HourSeconds : DaySeconds;
        DateTime fromUtc = ChartBuckets.AlignDown(requested.Value.FromUtc, bucketSeconds);
        DateTime toUtc = ChartBuckets.AlignDown(requested.Value.ToUtc.AddTicks(-1), bucketSeconds).AddSeconds(bucketSeconds);

        if (hourly && toUtc - fromUtc > MaxHourlyRange)
        {
            return Result<WorkspaceMessageHistory>.Failure(Error.Validation("TIME_RANGE_TOO_LONG", $"An hourly range can span at most {MaxHourlyRange.TotalDays:0} days."));
        }

        IReadOnlyCollection<MessageCountBucket> counted = await countAsync(fromUtc, toUtc, !hourly);

        return Result<WorkspaceMessageHistory>.Success(new WorkspaceMessageHistory(
            new DateTimeOffset(fromUtc, TimeSpan.Zero),
            new DateTimeOffset(toUtc, TimeSpan.Zero),
            bucketSeconds,
            Fill(counted, fromUtc, toUtc, TimeSpan.FromSeconds(bucketSeconds))));
    }

    // Every bucket in the range, so a chart needs no gap filling of its own.
    private static List<MessageCountBucket> Fill(IReadOnlyCollection<MessageCountBucket> counted, DateTime fromUtc, DateTime toUtc, TimeSpan size)
    {
        var byStart = counted.ToDictionary(b => b.Start.UtcDateTime);
        var buckets = new List<MessageCountBucket>((int)((toUtc - fromUtc) / size));
        for (DateTime start = fromUtc; start < toUtc; start += size)
        {
            buckets.Add(byStart.TryGetValue(start, out var bucket)
                ? bucket
                : new MessageCountBucket(new DateTimeOffset(start, TimeSpan.Zero), 0, 0));
        }

        return buckets;
    }
}
