using Wbskt.Gateway.Core.Services;

namespace Wbskt.Host.Tests.Gateway;

public sealed class SupersedeMonitorTests
{
    private static readonly Guid Device = Guid.NewGuid();

    [Fact]
    public void A_device_reconnecting_now_and_then_is_not_reported()
    {
        var time = new SettableTime(DateTimeOffset.UtcNow);
        var monitor = new SupersedeMonitor(time);

        for (var i = 0; i < 10; i++)
        {
            Assert.Null(monitor.Record(Device));
            time.Advance(TimeSpan.FromMinutes(3));
        }
    }

    [Fact]
    public void Repeated_supersedes_are_reported_once_per_window()
    {
        var time = new SettableTime(DateTimeOffset.UtcNow);
        var monitor = new SupersedeMonitor(time);

        var reports = new List<int?>();
        for (var i = 0; i < 20; i++)
        {
            reports.Add(monitor.Record(Device));
            time.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(SupersedeMonitor.Threshold, Assert.Single(reports, r => r is not null));

        time.Advance(SupersedeMonitor.Window);
        for (var i = 0; i < SupersedeMonitor.Threshold - 1; i++)
        {
            Assert.Null(monitor.Record(Device));
        }

        Assert.Equal(SupersedeMonitor.Threshold, monitor.Record(Device));
    }

    [Fact]
    public void Devices_are_counted_apart()
    {
        var monitor = new SupersedeMonitor(new SettableTime(DateTimeOffset.UtcNow));

        for (var i = 0; i < SupersedeMonitor.Threshold - 1; i++)
        {
            monitor.Record(Device);
            Assert.Null(monitor.Record(Guid.NewGuid()));
        }

        Assert.NotNull(monitor.Record(Device));
    }

    private sealed class SettableTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
