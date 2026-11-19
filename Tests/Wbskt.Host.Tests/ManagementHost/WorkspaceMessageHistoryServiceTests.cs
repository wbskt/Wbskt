using FluentAssertions;
using Moq;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Stats;
using Wbskt.Management.Models;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>The Overview's message chart: whole UTC buckets, every one listed, hours or days by the range.</summary>
public sealed class WorkspaceMessageHistoryServiceTests
{
    private const int WorkspaceId = 4;
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 25, 0, TimeSpan.Zero);

    private readonly Mock<IWorkspaceUsageProvider> _usage = new();

    private WorkspaceMessageHistoryService Service() => new(_usage.Object, new StoppedClock());

    [Fact]
    public async Task The_default_is_the_last_day_by_hour_with_empty_hours_as_zero()
    {
        var tenOClock = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        _usage.Setup(u => u.GetHourlyMessagesAsync(WorkspaceId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MessageCountBucket(tenOClock, 7, 2)]);

        var history = (await Service().GetAsync(WorkspaceId, null, null, null, default)).Value;

        history.BucketSeconds.Should().Be(3600);
        history.From.Should().Be(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
        history.To.Should().Be(new DateTimeOffset(2026, 11, 19, 11, 0, 0, TimeSpan.Zero));
        history.Buckets.Should().HaveCount(25);
        history.Buckets.Last().Should().Be(new MessageCountBucket(tenOClock, 7, 2));
        history.Buckets.SkipLast(1).Should().AllSatisfy(b => (b.Inbound, b.Outbound).Should().Be((0, 0)));
        _usage.Verify(u => u.GetHourlyMessagesAsync(WorkspaceId, history.From.UtcDateTime, history.To.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_range_past_two_days_is_counted_by_day()
    {
        _usage.Setup(u => u.GetDailyMessagesAsync(WorkspaceId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var history = (await Service().GetAsync(WorkspaceId, Now.AddDays(-7), Now, null, default)).Value;

        history.BucketSeconds.Should().Be(86400);
        history.Buckets.Should().HaveCount(8);
        _usage.Verify(u => u.GetDailyMessagesAsync(WorkspaceId, new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 19), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_hourly_range_past_a_month_is_refused()
    {
        var result = await Service().GetAsync(WorkspaceId, Now.AddDays(-40), Now, WorkspaceMessageHistoryService.Hour, default);

        result.Error.Code.Should().Be("TIME_RANGE_TOO_LONG");
    }

    [Fact]
    public async Task An_unknown_bucket_is_refused()
    {
        var result = await Service().GetAsync(WorkspaceId, null, null, "5m", default);

        result.Error.Code.Should().Be("MESSAGES_BUCKET_INVALID");
    }

    private sealed class StoppedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
