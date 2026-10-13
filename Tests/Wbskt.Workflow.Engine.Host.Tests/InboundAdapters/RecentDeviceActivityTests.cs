using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class RecentDeviceActivityTests
{
    private readonly SteppedClock _clock = new(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_device_counts_as_recently_active_for_the_window_after_it_is_heard()
    {
        var activity = new RecentDeviceActivity(_clock);
        Guid device = Guid.NewGuid();

        Assert.False(activity.WasRecentlyActive(device));

        activity.Saw(device);
        _clock.Advance(RecentDeviceActivity.Window);
        Assert.True(activity.WasRecentlyActive(device));

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(activity.WasRecentlyActive(device));
    }

    [Fact]
    public void Devices_gone_quiet_are_forgotten_so_the_record_stays_small()
    {
        var activity = new RecentDeviceActivity(_clock);
        for (int i = 0; i < 100; i++)
        {
            activity.Saw(Guid.NewGuid());
        }

        _clock.Advance(RecentDeviceActivity.Window + TimeSpan.FromSeconds(1));
        Guid latest = Guid.NewGuid();
        activity.Saw(latest);

        Assert.Equal(1, activity.Count);
        Assert.True(activity.WasRecentlyActive(latest));
    }

    private sealed class SteppedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
