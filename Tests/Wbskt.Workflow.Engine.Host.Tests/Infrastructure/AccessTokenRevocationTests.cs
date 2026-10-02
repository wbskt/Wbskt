using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

public sealed class AccessTokenRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_token_issued_before_the_revocation_is_refused()
    {
        var (revocation, _) = Create();

        await revocation.RevokeUserAsync(7);

        Assert.True(revocation.IsRevoked(7, Now.AddMinutes(-5)));
        Assert.True(revocation.IsRevoked(7, Now.AddSeconds(-1)));
    }

    [Fact]
    public async Task A_token_issued_after_the_revocation_is_accepted()
    {
        var (revocation, _) = Create();

        await revocation.RevokeUserAsync(7);

        Assert.False(revocation.IsRevoked(7, Now.AddSeconds(1)));
    }

    [Fact]
    public async Task A_token_from_the_revocations_own_second_is_accepted()
    {
        // iat cannot tell "just before" from "just after" within a second; the later login wins.
        var (revocation, _) = Create();

        await revocation.RevokeUserAsync(7);

        Assert.False(revocation.IsRevoked(7, Now));
    }

    [Fact]
    public async Task Other_users_are_untouched()
    {
        var (revocation, _) = Create();

        await revocation.RevokeUserAsync(7);

        Assert.False(revocation.IsRevoked(8, Now.AddMinutes(-5)));
    }

    [Fact]
    public async Task A_later_revocation_moves_the_watermark_forward()
    {
        var (revocation, time) = Create();
        await revocation.RevokeUserAsync(7);

        time.Advance(TimeSpan.FromMinutes(2));
        await revocation.RevokeUserAsync(7);

        Assert.True(revocation.IsRevoked(7, Now.AddMinutes(1)));
    }

    [Fact]
    public async Task An_entry_is_dropped_once_no_token_it_could_refuse_is_still_alive()
    {
        var (revocation, time) = Create(TimeSpan.FromMinutes(15));
        await revocation.RevokeUserAsync(7);

        time.Advance(TimeSpan.FromMinutes(17));
        await revocation.ResyncAsync();

        // Anything issued before the revocation has expired by now, so the entry has nothing left to do.
        Assert.False(revocation.IsRevoked(7, Now.AddMinutes(-1)));
    }

    [Fact]
    public async Task An_entry_is_kept_while_tokens_it_refuses_could_still_be_alive()
    {
        var (revocation, time) = Create(TimeSpan.FromMinutes(15));
        await revocation.RevokeUserAsync(7);

        time.Advance(TimeSpan.FromMinutes(14));
        await revocation.ResyncAsync();

        Assert.True(revocation.IsRevoked(7, Now.AddMinutes(-1)));
    }

    private static (AccessTokenRevocation Revocation, ManualTime Time) Create(TimeSpan? lifetime = null)
    {
        var time = new ManualTime(Now);
        var options = Options.Create(new AccessTokenOptions { AccessTokenLifetime = lifetime ?? TimeSpan.FromMinutes(15) });
        return (new AccessTokenRevocation(null, options, time, NullLogger<AccessTokenRevocation>.Instance), time);
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
