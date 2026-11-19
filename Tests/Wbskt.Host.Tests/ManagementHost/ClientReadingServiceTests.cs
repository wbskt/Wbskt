using Moq;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Readings;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class ClientReadingServiceTests
{
    private const int WorkspaceId = 7;
    private const int ClientId = 42;
    private static readonly Guid ClientRef = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 34, 56, TimeSpan.Zero);

    private readonly Mock<IClientReadingProvider> _provider = new();

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    [InlineData("1d", 86400)]
    [InlineData("1D", 86400)]
    [InlineData("90", 90)]
    public void Bucket_sizes_parse(string text, int seconds)
    {
        Assert.True(ChartBuckets.TryParse(text, ClientReadingService.MaxRange, out var parsed));
        Assert.Equal(seconds, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5m")]
    [InlineData("5x")]
    [InlineData("1.5h")]
    [InlineData("800d")]
    [InlineData("99999999999999999999")]
    public void Bad_bucket_sizes_are_refused(string text)
    {
        Assert.False(ChartBuckets.TryParse(text, ClientReadingService.MaxRange, out _));
    }

    [Theory]
    [InlineData(1, 300)]      // a day in 5-minute buckets: 288
    [InlineData(7, 1800)]     // a week in half hours: 336
    [InlineData(365, 86400)]  // a year in days
    public void Without_a_bucket_one_is_picked_that_keeps_the_chart_readable(int days, int expected)
    {
        Assert.Equal(expected, ClientReadingService.PickBucket(TimeSpan.FromDays(days)));
    }

    [Fact]
    public async Task The_range_defaults_to_the_last_day_and_aligns_to_the_bucket()
    {
        var result = await Service().GetBucketsAsync(WorkspaceId, ClientRef, "temp", null, null, "1h");

        Assert.True(result.IsSuccess);
        var from = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(from, result.Value.From);
        Assert.Equal(Now.UtcDateTime, result.Value.To);
        Assert.Equal(3600, result.Value.BucketSeconds);
        _provider.Verify(p => p.GetBucketsAsync(ClientId, "temp", from, Now.UtcDateTime, 3600, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_explicit_range_is_read_in_utc()
    {
        var from = new DateTimeOffset(2026, 10, 1, 5, 30, 0, TimeSpan.FromHours(5.5));
        var to = new DateTimeOffset(2026, 10, 2, 5, 30, 0, TimeSpan.FromHours(5.5));

        var result = await Service().GetBucketsAsync(WorkspaceId, ClientRef, "temp", from, to, "5m");

        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), result.Value.From);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), result.Value.To);
    }

    [Theory]
    [InlineData(null, null, null, null, "READINGS_NAME_REQUIRED")]
    [InlineData("temp", null, null, "5x", "READINGS_BUCKET_INVALID")]
    [InlineData("temp", null, null, "1s", "READINGS_TOO_MANY_BUCKETS")]
    [InlineData("temp", -1, -2, null, "TIME_RANGE_INVALID")]
    [InlineData("temp", -800, null, null, "TIME_RANGE_TOO_LONG")]
    public async Task Bad_queries_are_refused(string? name, int? fromDays, int? toDays, string? bucket, string code)
    {
        var result = await Service().GetBucketsAsync(WorkspaceId, ClientRef, name,
            fromDays is null ? null : Now.AddDays(fromDays.Value), toDays is null ? null : Now.AddDays(toDays.Value), bucket);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        _provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_export_that_would_be_cut_off_is_refused()
    {
        var reading = new ClientReading("temp", Now.UtcDateTime, Now.UtcDateTime, 1, false);
        _provider.Setup(p => p.GetRangeAsync(ClientId, null, It.IsAny<DateTime>(), It.IsAny<DateTime>(), ClientReadingService.MaxExportRows + 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Repeat(reading, ClientReadingService.MaxExportRows + 1).ToList());

        var result = await Service().GetRangeAsync(WorkspaceId, ClientRef, null, null, null);

        Assert.Equal("READINGS_RANGE_TOO_LARGE", result.Error.Code);
    }

    [Fact]
    public async Task An_export_that_fits_is_returned_whole()
    {
        var reading = new ClientReading("temp", Now.UtcDateTime, Now.UtcDateTime, 1, false);
        _provider.Setup(p => p.GetRangeAsync(ClientId, "temp", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([reading]);

        var result = await Service().GetRangeAsync(WorkspaceId, ClientRef, "temp", null, null);

        Assert.Equal([reading], result.Value);
    }

    private ClientReadingService Service()
    {
        var clients = new Mock<IClientQueryService>();
        clients.Setup(c => c.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRef, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(ClientId));
        return new ClientReadingService(_provider.Object, clients.Object, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task The_hourly_export_is_one_row_per_variable_and_hour()
    {
        var hour = new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc);
        _provider.Setup(p => p.GetHourlyRangeAsync(ClientId, null, It.IsAny<DateTime>(), It.IsAny<DateTime>(), ClientReadingService.MaxExportRows + 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClientReadingHour("temp", hour, 4, 1.5, 2, 3)]);

        var result = await Service().ExportCsvAsync(WorkspaceId, ClientRef, null, null, null, "Hourly");

        Assert.Equal("name,hourStart,count,min,avg,max", result.Value.Header);
        Assert.Equal(["temp,2026-09-01T03:00:00.000Z,4,1.5,2,3"], await result.Value.Rows.ToListAsync());
    }

    [Fact]
    public async Task An_unknown_resolution_is_refused()
    {
        var result = await Service().ExportCsvAsync(WorkspaceId, ClientRef, null, null, null, "daily");

        Assert.Equal("READINGS_RESOLUTION_INVALID", result.Error.Code);
        _provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Another_workspaces_client_is_not_found_and_nothing_is_read()
    {
        var clients = new Mock<IClientQueryService>();
        clients.Setup(c => c.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure(Error.NotFound("CLIENT_NOT_FOUND", "Client not found.")));
        var service = new ClientReadingService(_provider.Object, clients.Object, new FixedTimeProvider(Now));

        var buckets = await service.GetBucketsAsync(WorkspaceId, ClientRef, "temp", null, null, null);
        var range = await service.GetRangeAsync(WorkspaceId, ClientRef, null, null, null);

        Assert.Equal("CLIENT_NOT_FOUND", buckets.Error.Code);
        Assert.Equal("CLIENT_NOT_FOUND", range.Error.Code);
        _provider.VerifyNoOtherCalls();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
