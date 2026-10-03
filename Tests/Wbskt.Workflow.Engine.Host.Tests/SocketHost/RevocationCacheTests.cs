using FluentAssertions;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Tests.SocketHost;

public sealed class RevocationCacheTests
{
    [Fact]
    public void Unknown_client_is_not_revoked()
    {
        var cache = new RevocationCache();

        cache.IsRevoked(Guid.NewGuid(), DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Revoked_client_is_reported_revoked()
    {
        var cache = new RevocationCache();
        var clientRefId = Guid.NewGuid();

        cache.Revoke(clientRefId);

        cache.IsRevoked(clientRefId, DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void Cleared_client_may_connect_again()
    {
        var cache = new RevocationCache();
        var clientRefId = Guid.NewGuid();
        cache.Revoke(clientRefId);

        cache.Clear(clientRefId);

        cache.IsRevoked(clientRefId, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void A_rotated_secret_refuses_tokens_issued_before_it_and_accepts_later_ones()
    {
        var cache = new RevocationCache();
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
        var cache = new RevocationCache();
        var clientRefId = Guid.NewGuid();
        cache.Revoke(clientRefId);

        cache.RevokeIssuedBefore(clientRefId, DateTime.UtcNow);

        cache.IsRevoked(clientRefId, DateTime.UtcNow.AddMinutes(1)).Should().BeTrue();
    }
}
