using Wbskt.Protocol;

namespace Wbskt.Host.Tests.ClientSdk;

public sealed class TokenBucketTests
{
    [Fact]
    public void After_the_burst_the_next_token_is_one_interval_away()
    {
        var time = new ManualTime();
        var bucket = new TokenBucket(ClientProtocol.SendPerSecond, ClientProtocol.SendBurst, time);

        Assert.Equal(ClientProtocol.SendBurst, Enumerable.Range(0, 100).Count(_ => bucket.TryTake()));
        Assert.Equal(TimeSpan.FromSeconds(1 / ClientProtocol.SendPerSecond), bucket.TimeUntilNext());

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.Zero, bucket.TimeUntilNext());
        Assert.Equal(8, Enumerable.Range(0, 20).Count(_ => bucket.TryTake()));
    }

    [Fact]
    public void The_sdk_pace_stays_under_the_gateway_default_budget()
    {
        var gateway = new Wbskt.Gateway.Core.Infrastructure.MessageBudgetOptions();

        Assert.True(ClientProtocol.SendPerSecond < gateway.PerSecond);
        Assert.True(ClientProtocol.SendBurst < gateway.Burst);
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
