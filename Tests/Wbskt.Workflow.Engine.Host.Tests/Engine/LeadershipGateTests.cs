using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Engine;

public sealed class LeadershipGateTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Active_once_leader_with_the_bus_started()
    {
        var state = new LeadershipState();
        state.SetLeader(Now.AddSeconds(30));
        state.SetBusStarted();

        Assert.True(new LeadershipGate(state, new FixedClock()).IsActive);
    }

    [Fact]
    public void Not_active_before_the_bus_starts()
    {
        var state = new LeadershipState();
        state.SetLeader(Now.AddSeconds(30));

        Assert.False(new LeadershipGate(state, new FixedClock()).IsActive);
    }

    [Fact]
    public void Not_active_within_the_safety_margin_of_the_lease_expiring()
    {
        var state = new LeadershipState();
        state.SetLeader(Now.AddSeconds(4));
        state.SetBusStarted();

        Assert.False(new LeadershipGate(state, new FixedClock()).IsActive);
    }

    [Fact]
    public void Not_active_on_standby()
    {
        var state = new LeadershipState();
        state.SetLeader(Now.AddSeconds(30));
        state.SetBusStarted();
        state.SetStandby();

        Assert.False(new LeadershipGate(state, new FixedClock()).IsActive);
    }

    [Fact]
    public void Always_active_outside_the_engine_host()
    {
        Assert.True(new AlwaysActiveLeadershipGate().IsActive);
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => Now;
    }
}
