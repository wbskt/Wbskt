using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Management.Host.Models;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Stats;
using Wbskt.Management.Models;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>The Overview's message chart: whole UTC buckets, every one listed, hours or days by the range.</summary>
public sealed class WorkspaceMessageHistoryServiceTests
{
    private const int WorkspaceId = 4;
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 25, 0, TimeSpan.Zero);

    private readonly Mock<IWorkspaceUsageProvider> _usage = new();
    private readonly Mock<IClientQueryService> _clients = new();

    private WorkspaceMessageHistoryService Service() => new(_usage.Object, _clients.Object, new StoppedClock(), Options.Create(new EventLoggingOptions { RetentionDays = 90 }));

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

    [Theory]
    [InlineData("60m")]
    [InlineData("3600")]
    [InlineData("1H")]
    public async Task An_hour_is_read_in_the_grammar_every_chart_takes(string bucket)
    {
        _usage.Setup(u => u.GetHourlyMessagesAsync(WorkspaceId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var history = (await Service().GetAsync(WorkspaceId, null, null, bucket, default)).Value;

        history.BucketSeconds.Should().Be(3600);
    }

    [Fact]
    public async Task A_short_range_older_than_the_hourly_counts_is_counted_by_day()
    {
        // #1081: it used to answer 24 zero hours for a day that held traffic.
        _usage.Setup(u => u.GetDailyMessagesAsync(WorkspaceId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var history = (await Service().GetAsync(WorkspaceId, Now.AddDays(-120), Now.AddDays(-119), null, default)).Value;

        history.BucketSeconds.Should().Be(86400);
        _usage.Verify(u => u.GetHourlyMessagesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Hours_asked_for_past_their_retention_are_refused()
    {
        var result = await Service().GetAsync(WorkspaceId, Now.AddDays(-120), Now.AddDays(-119), WorkspaceMessageHistoryService.Hour, default);

        result.Error.Code.Should().Be("MESSAGES_HOURLY_EXPIRED");
    }

    [Fact]
    public async Task A_clients_last_week_is_counted_by_day_from_its_hours()
    {
        Guid clientRef = Guid.NewGuid();
        _clients.Setup(c => c.EnsureClientInWorkspaceAsync(WorkspaceId, clientRef, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(31));
        var monday = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        _usage.Setup(u => u.GetClientMessagesAsync(31, It.IsAny<DateTime>(), It.IsAny<DateTime>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MessageCountBucket(monday, 40, 3)]);

        var history = (await Service().GetForClientAsync(WorkspaceId, clientRef, Now.AddDays(-7), Now, null, default)).Value;

        history.BucketSeconds.Should().Be(86400);
        history.Buckets.Should().HaveCount(8);
        history.Buckets.Should().ContainSingle(b => b.Inbound == 40).Which.Start.Should().Be(monday);
        _usage.Verify(u => u.GetDailyMessagesAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_clients_days_go_with_its_hours()
    {
        // A client's day is the sum of its hours, so past their retention there is nothing to sum.
        Guid clientRef = Guid.NewGuid();
        _clients.Setup(c => c.EnsureClientInWorkspaceAsync(WorkspaceId, clientRef, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(31));

        var result = await Service().GetForClientAsync(WorkspaceId, clientRef, Now.AddDays(-120), Now, "1d", default);

        result.Error.Code.Should().Be("MESSAGES_HOURLY_EXPIRED");
        _usage.Verify(u => u.GetClientMessagesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Another_workspaces_client_is_not_found()
    {
        Guid clientRef = Guid.NewGuid();
        _clients.Setup(c => c.EnsureClientInWorkspaceAsync(WorkspaceId, clientRef, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Failure(Error.NotFound("CLIENT_NOT_FOUND", "Client not found.")));

        var result = await Service().GetForClientAsync(WorkspaceId, clientRef, null, null, null, default);

        result.Error.Code.Should().Be("CLIENT_NOT_FOUND");
    }

    private sealed class StoppedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
