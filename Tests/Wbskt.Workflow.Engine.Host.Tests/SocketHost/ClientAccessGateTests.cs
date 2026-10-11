using System.Net.WebSockets;
using Moq;
using Wbskt.Infrastructure.Security;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Tests.SocketHost;

public sealed class ClientAccessGateTests
{
    private static readonly Guid ClientRefId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly Mock<IClientTokenCutoffs> _redis = new();
    private readonly Mock<IConnectionManager> _connections = new();
    private readonly ClientAccessGate _gate;

    public ClientAccessGateTests()
    {
        // Redis cannot answer: this host's own list decides.
        _redis.Setup(x => x.IsRevokedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((bool?)null);
        _gate = new ClientAccessGate(new RevocationCache(), _redis.Object, _connections.Object);
    }

    [Fact]
    public async Task Reapproval_after_a_rotation_keeps_refusing_the_old_secrets_tokens()
    {
        var rotatedAt = DateTime.UtcNow.AddMinutes(-10);
        var oldSecretToken = rotatedAt.AddMinutes(-5);
        await _gate.RevokeIssuedBeforeAsync(ClientRefId, rotatedAt, "rotated");
        await _gate.RevokeAsync(ClientRefId, "revoked");

        _gate.Reinstate(ClientRefId, DateTime.UtcNow);

        Assert.True(await _gate.IsRefusedAsync(ClientRefId, oldSecretToken));
        Assert.False(await _gate.IsRefusedAsync(ClientRefId, DateTime.UtcNow.AddSeconds(5)));
    }

    [Fact]
    public async Task Redis_refusing_is_enough_even_when_this_host_never_heard_of_the_revocation()
    {
        _redis.Setup(x => x.IsRevokedAsync(ClientRefId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True(await _gate.IsRefusedAsync(ClientRefId, DateTime.UtcNow));
    }

    [Fact]
    public async Task Revoking_closes_the_live_connection_as_revoked()
    {
        await _gate.RevokeAsync(ClientRefId, "Client deleted.");

        _connections.Verify(x => x.RequestCloseAsync(ClientRefId, DisconnectReason.Revoked, WebSocketCloseStatus.PolicyViolation, "Client deleted."), Times.Once);
    }
}
