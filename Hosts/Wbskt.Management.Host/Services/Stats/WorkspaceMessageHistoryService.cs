using Wbskt.Infrastructure;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Stats;

/// <summary>
/// Message counts for the Overview chart, from the per-hour and per-day ledgers dbo.ClientTraffic_InsertBatch
/// keeps, so a chart reads at most a few hundred rows however much traffic the range held.
/// </summary>
internal sealed class WorkspaceMessageHistoryService(IWorkspaceUsageProvider usage, TimeProvider clock) : IWorkspaceMessageHistoryService
{
    internal const string Hour = "1h";
    internal const string Day = "1d";

    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(1);

    // Hourly counts are kept as long as the audit log (90 days); a month of hours is 744 buckets.
    private static readonly TimeSpan MaxHourlyRange = TimeSpan.FromDays(31);
    private static readonly TimeSpan MaxDailyRange = TimeSpan.FromDays(366);

    // Up to this span, a bucket left out means hours.
    private static readonly TimeSpan HourlyUpTo = TimeSpan.FromDays(2);

    public async Task<Result<WorkspaceMessageHistory>> GetAsync(int workspaceId, DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken ct)
    {
        if (bucket is not null && bucket != Hour && bucket != Day)
        {
            return Result<WorkspaceMessageHistory>.Failure(Error.Validation("MESSAGES_BUCKET_INVALID", "'bucket' must be 1h or 1d."));
        }

        Result<TimeRange> requested = TimeRange.Resolve(from, to, clock.GetUtcNow(), DefaultRange, MaxDailyRange);
        if (requested.IsFailure)
        {
            return Result<WorkspaceMessageHistory>.Failure(requested.Error);
        }

        bool hourly = bucket is null ? requested.Value.ToUtc - requested.Value.FromUtc <= HourlyUpTo : bucket == Hour;
        TimeSpan size = hourly ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        DateTime fromUtc = Floor(requested.Value.FromUtc, size);
        DateTime toUtc = Floor(requested.Value.ToUtc.AddTicks(-1), size) + size;

        if (hourly && toUtc - fromUtc > MaxHourlyRange)
        {
            return Result<WorkspaceMessageHistory>.Failure(Error.Validation("TIME_RANGE_TOO_LONG", $"An hourly range can span at most {MaxHourlyRange.TotalDays:0} days."));
        }

        IReadOnlyCollection<MessageCountBucket> counted = hourly
            ? await usage.GetHourlyMessagesAsync(workspaceId, fromUtc, toUtc, ct)
            : await usage.GetDailyMessagesAsync(workspaceId, DateOnly.FromDateTime(fromUtc), DateOnly.FromDateTime(toUtc.AddDays(-1)), ct);

        return Result<WorkspaceMessageHistory>.Success(new WorkspaceMessageHistory(
            new DateTimeOffset(fromUtc, TimeSpan.Zero),
            new DateTimeOffset(toUtc, TimeSpan.Zero),
            (int)size.TotalSeconds,
            Fill(counted, fromUtc, toUtc, size)));
    }

    private static DateTime Floor(DateTime utc, TimeSpan size) =>
        new(utc.Ticks - utc.Ticks % size.Ticks, DateTimeKind.Utc);

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
