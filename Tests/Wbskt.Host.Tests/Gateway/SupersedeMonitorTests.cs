using Wbskt.Gateway.Core.Services;

namespace Wbskt.Host.Tests.Gateway;

public sealed class SupersedeMonitorTests
{
    private static readonly Guid Client = Guid.NewGuid();

    [Fact]
    public void A_client_reconnecting_now_and_then_is_not_reported()
    {
        var time = new SettableTime(DateTimeOffset.UtcNow);
        var monitor = new SupersedeMonitor(time);

        for (var i = 0; i < 10; i++)
        {
            Assert.Null(monitor.Record(Client));
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
            reports.Add(monitor.Record(Client));
            time.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(SupersedeMonitor.Threshold, Assert.Single(reports, r => r is not null));

        time.Advance(SupersedeMonitor.Window);
        for (var i = 0; i < SupersedeMonitor.Threshold - 1; i++)
        {
            Assert.Null(monitor.Record(Client));
        }

        Assert.Equal(SupersedeMonitor.Threshold, monitor.Record(Client));
    }

    [Fact]
    public void Clients_are_counted_apart()
    {
        var monitor = new SupersedeMonitor(new SettableTime(DateTimeOffset.UtcNow));

        for (var i = 0; i < SupersedeMonitor.Threshold - 1; i++)
        {
            monitor.Record(Client);
            Assert.Null(monitor.Record(Guid.NewGuid()));
        }

        Assert.NotNull(monitor.Record(Client));
    }

    private sealed class SettableTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
