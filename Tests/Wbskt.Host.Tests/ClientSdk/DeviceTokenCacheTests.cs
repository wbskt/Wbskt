using Wbskt.Client.Sdk.Internal;

namespace Wbskt.Host.Tests.ClientSdk;

public sealed class DeviceTokenCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_reconnect_inside_the_tokens_lifetime_does_not_log_in()
    {
        var time = new SettableTime(Now);
        var logins = 0;
        var cache = new DeviceTokenCache(() => Task.FromResult(new DeviceToken($"t{++logins}", time.Now.AddMinutes(15))), time);

        Assert.Equal("t1", await cache.GetAsync());
        time.Now = Now.AddMinutes(10);
        Assert.Equal("t1", await cache.GetAsync());

        Assert.Equal(1, logins);
    }

    [Fact]
    public async Task A_token_about_to_expire_is_renewed()
    {
        var time = new SettableTime(Now);
        var logins = 0;
        var cache = new DeviceTokenCache(() => Task.FromResult(new DeviceToken($"t{++logins}", Now.AddMinutes(15))), time);

        await cache.GetAsync();
        time.Now = Now.AddMinutes(14).AddSeconds(30);

        Assert.Equal("t2", await cache.GetAsync());
    }

    [Fact]
    public async Task A_rejected_token_is_replaced_on_the_next_attempt()
    {
        var logins = 0;
        var cache = new DeviceTokenCache(() => Task.FromResult(new DeviceToken($"t{++logins}", Now.AddMinutes(15))), new SettableTime(Now));

        await cache.GetAsync();
        cache.Invalidate();

        Assert.Equal("t2", await cache.GetAsync());
    }

    private sealed class SettableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
