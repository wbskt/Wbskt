using FluentAssertions;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

public sealed class RevocationCacheTests
{
    [Fact]
    public void Unknown_client_is_not_revoked()
    {
        var cache = new RevocationCache(TimeProvider.System);

        cache.IsRevoked(Guid.NewGuid(), DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Revoked_client_is_reported_revoked()
    {
        var cache = new RevocationCache(TimeProvider.System);
        var clientRefId = Guid.NewGuid();

        cache.Revoke(clientRefId);

        cache.IsRevoked(clientRefId, DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void A_reinstated_client_may_connect_with_a_token_from_then_on()
    {
        var cache = new RevocationCache(TimeProvider.System);
        var clientRefId = Guid.NewGuid();
        var reinstatedAt = DateTime.UtcNow;
        cache.Revoke(clientRefId);

        cache.Reinstate(clientRefId, reinstatedAt);

        cache.IsRevoked(clientRefId, reinstatedAt.AddSeconds(1)).Should().BeFalse();
        cache.IsRevoked(clientRefId, reinstatedAt.AddMinutes(-1)).Should().BeTrue();
    }

    [Fact]
    public void A_rotated_secret_refuses_tokens_issued_before_it_and_accepts_later_ones()
    {
        var cache = new RevocationCache(TimeProvider.System);
        var clientRefId = Guid.NewGuid();
        var rotatedAt = new DateTime(2026, 10, 3, 12, 0, 0, 500, DateTimeKind.Utc);

        cache.RevokeIssuedBefore(clientRefId, rotatedAt);

        cache.IsRevoked(clientRefId, rotatedAt.AddSeconds(-2)).Should().BeTrue();
        // Issue times are whole seconds: a token minted with the new secret in the rotation's own
        // second carries that second, and must get in.
        cache.IsRevoked(clientRefId, new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc)).Should().BeFalse();
        cache.IsRevoked(clientRefId, rotatedAt.AddSeconds(5)).Should().BeFalse();
    }

    [Fact]
    public void A_rotation_does_not_lift_a_full_revocation()
    {
        var cache = new RevocationCache(TimeProvider.System);
        var clientRefId = Guid.NewGuid();
        cache.Revoke(clientRefId);

        cache.RevokeIssuedBefore(clientRefId, DateTime.UtcNow);

        cache.IsRevoked(clientRefId, DateTime.UtcNow.AddMinutes(1)).Should().BeTrue();
    }

    [Fact]
    public void An_entry_past_its_retention_lets_the_client_in_and_is_dropped()
    {
        var clock = new SteppedClock(new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));
        var cache = new RevocationCache(clock);
        var clientRefId = Guid.NewGuid();
        cache.Revoke(clientRefId);

        clock.Now = clock.Now.AddMinutes(71);

        cache.IsRevoked(clientRefId, clock.Now).Should().BeFalse();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void A_write_drops_the_entries_past_their_retention_that_are_never_read_again()
    {
        var clock = new SteppedClock(new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));
        var cache = new RevocationCache(clock);
        cache.Revoke(Guid.NewGuid());
        cache.Revoke(Guid.NewGuid());

        clock.Now = clock.Now.AddMinutes(71);
        cache.Revoke(Guid.NewGuid());

        cache.Count.Should().Be(1);
    }

    private sealed class SteppedClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
