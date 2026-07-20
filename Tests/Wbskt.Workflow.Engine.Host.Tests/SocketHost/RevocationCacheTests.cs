using FluentAssertions;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Tests.SocketHost;

public sealed class RevocationCacheTests
{
    [Fact]
    public void Unknown_client_is_not_revoked()
    {
        var cache = new RevocationCache();

        cache.IsRevoked(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Revoked_client_is_reported_revoked()
    {
        var cache = new RevocationCache();
        var clientRefId = Guid.NewGuid();

        cache.Revoke(clientRefId);

        cache.IsRevoked(clientRefId).Should().BeTrue();
    }

    [Fact]
    public void Cleared_client_may_connect_again()
    {
        var cache = new RevocationCache();
        var clientRefId = Guid.NewGuid();
        cache.Revoke(clientRefId);

        cache.Clear(clientRefId);

        cache.IsRevoked(clientRefId).Should().BeFalse();
    }
}
