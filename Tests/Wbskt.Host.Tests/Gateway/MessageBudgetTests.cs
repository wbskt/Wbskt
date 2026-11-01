using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

public sealed class MessageBudgetTests
{
    [Fact]
    public void A_burst_is_allowed_and_the_excess_dropped()
    {
        var time = new ManualTime();
        var budget = new MessageBudget(new MessageBudgetOptions { PerSecond = 10, Burst = 5, CloseAfterDropped = 100 }, time);

        var passed = Enumerable.Range(0, 8).Count(_ => budget.TryTake());

        Assert.Equal(5, passed);
        Assert.Equal(3, budget.ConsecutiveDropped);
        Assert.False(budget.Exhausted);
    }

    [Fact]
    public void The_budget_refills_with_time_and_a_message_through_resets_the_drop_count()
    {
        var time = new ManualTime();
        var budget = new MessageBudget(new MessageBudgetOptions { PerSecond = 10, Burst = 1, CloseAfterDropped = 100 }, time);
        Assert.True(budget.TryTake());
        Assert.False(budget.TryTake());

        time.Advance(TimeSpan.FromMilliseconds(100));

        Assert.True(budget.TryTake());
        Assert.Equal(0, budget.ConsecutiveDropped);
    }

    [Fact]
    public void A_device_in_a_tight_loop_exhausts_its_budget()
    {
        var time = new ManualTime();
        var budget = new MessageBudget(new MessageBudgetOptions { PerSecond = 10, Burst = 10, CloseAfterDropped = 50 }, time);

        for (var i = 0; i < 60; i++)
        {
            budget.TryTake();
        }

        Assert.True(budget.Exhausted);
    }

    [Fact]
    public void A_device_within_its_rate_never_loses_a_message()
    {
        var time = new ManualTime();
        var budget = new MessageBudget(new MessageBudgetOptions { PerSecond = 10, Burst = 1, CloseAfterDropped = 5 }, time);

        for (var i = 0; i < 1000; i++)
        {
            Assert.True(budget.TryTake());
            time.Advance(TimeSpan.FromMilliseconds(100));
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
