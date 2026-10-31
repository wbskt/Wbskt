using Wbskt.Client.Sdk.Internal;

namespace Wbskt.Host.Tests.ClientSdk;

public sealed class ReconnectPolicyTests
{
    [Fact]
    public void A_fleet_that_drops_together_spreads_its_first_attempts()
    {
        var policy = new ReconnectPolicy(new Random(445));

        var perSecond = Enumerable.Range(0, 1000)
            .Select(_ => policy.FirstDelay(serverRestarting: false))
            .GroupBy(d => (int)d.TotalSeconds)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.All(perSecond.Keys, second => Assert.InRange(second, 0, (int)ReconnectPolicy.Spread.TotalSeconds - 1));
        Assert.All(perSecond.Values, count => Assert.True(count <= 150, $"{count} of 1000 in one second"));
    }

    [Fact]
    public void A_server_restart_spreads_over_the_longer_window()
    {
        var policy = new ReconnectPolicy(new Random(1));

        var delays = Enumerable.Range(0, 1000).Select(_ => policy.FirstDelay(serverRestarting: true)).ToList();

        Assert.All(delays, d => Assert.True(d < ReconnectPolicy.RestartSpread));
        Assert.Contains(delays, d => d > ReconnectPolicy.Spread);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(3, 16)]
    [InlineData(5, 60)]
    [InlineData(50, 60)]
    public void Retries_back_off_exponentially_up_to_the_cap(int failures, int ceilingSeconds)
    {
        var policy = new ReconnectPolicy(new Random(7));

        var delays = Enumerable.Range(0, 500).Select(_ => policy.RetryDelay(failures)).ToList();

        Assert.All(delays, d => Assert.True(d <= TimeSpan.FromSeconds(ceilingSeconds)));
        Assert.Contains(delays, d => d > TimeSpan.FromSeconds(ceilingSeconds) / 2);
    }

    [Fact]
    public void A_throttled_login_waits_at_least_what_the_server_asked()
    {
        var policy = new ReconnectPolicy(new Random(3));

        var delay = policy.AfterThrottle(TimeSpan.FromSeconds(20));

        Assert.InRange(delay, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20) + ReconnectPolicy.Base);
    }
}
