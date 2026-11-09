using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

public sealed class MessageThrottleTests
{
    private static readonly DateTime Start = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Client = Guid.NewGuid();
    private readonly MovableClock _clock = new(Start);

    [Fact]
    public void A_listed_workspace_is_throttled_and_others_are_not()
    {
        var throttle = new MessageThrottle(_clock);

        throttle.Replace([7], Start);

        Assert.True(throttle.IsThrottled(7));
        Assert.False(throttle.IsThrottled(8));
    }

    [Fact]
    public void A_list_the_management_host_stopped_sending_lets_go_of_its_workspaces()
    {
        var throttle = new MessageThrottle(_clock);
        throttle.Replace([7], Start);

        _clock.Now = Start + MessageThrottle.StaleAfter;

        Assert.False(throttle.IsThrottled(7));
    }

    [Fact]
    public void An_older_list_arriving_late_does_not_replace_a_newer_one()
    {
        var throttle = new MessageThrottle(_clock);
        throttle.Replace([], Start);

        throttle.Replace([7], Start.AddMinutes(-1));

        Assert.False(throttle.IsThrottled(7));
    }

    [Fact]
    public void A_throttled_client_gets_one_message_through_a_minute()
    {
        var throttle = new MessageThrottle(_clock);
        throttle.Replace([7], Start);
        var allowance = new ClientAllowance(throttle, 7, Client);

        Assert.True(allowance.TryTake());
        Assert.False(allowance.TryTake());
        Assert.False(allowance.TryTake());
        Assert.Equal(2, allowance.ConsecutiveHeldBack);

        _clock.Now = Start.AddMinutes(1);
        throttle.Replace([7], _clock.Now);

        Assert.True(allowance.TryTake());
        Assert.Equal(0, allowance.ConsecutiveHeldBack);
    }

    [Fact]
    public void A_client_of_a_workspace_that_is_not_throttled_sends_freely()
    {
        var throttle = new MessageThrottle(_clock);
        throttle.Replace([8], Start);
        var allowance = new ClientAllowance(throttle, 7, Client);

        Assert.All(Enumerable.Range(0, 10), _ => Assert.True(allowance.TryTake()));
    }

    [Fact]
    public void A_client_that_reconnects_does_not_get_a_fresh_message_but_another_client_does()
    {
        var throttle = new MessageThrottle(_clock);
        throttle.Replace([7], Start);

        Assert.True(new ClientAllowance(throttle, 7, Client).TryTake());
        Assert.False(new ClientAllowance(throttle, 7, Client).TryTake());
        Assert.True(new ClientAllowance(throttle, 7, Guid.NewGuid()).TryTake());
    }

    private sealed class MovableClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
