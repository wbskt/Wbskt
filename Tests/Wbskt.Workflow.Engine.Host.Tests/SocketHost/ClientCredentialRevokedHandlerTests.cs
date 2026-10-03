using System.Net.WebSockets;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Handlers;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Tests.SocketHost;

public sealed class ClientCredentialRevokedHandlerTests
{
    private static readonly Guid ClientRefId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task A_deleted_client_is_refused_and_disconnected()
    {
        var connections = new Mock<IConnectionManager>();
        var cache = new Mock<IRevocationCache>();
        var handler = new ClientDeletedHandler(connections.Object, cache.Object, NullLogger<ClientDeletedHandler>.Instance);

        await handler.Consume(Context(new ClientDeletedEvent(ClientRefId, 42, Guid.NewGuid(), 5, 7, "device")));

        cache.Verify(x => x.Revoke(ClientRefId), Times.Once);
        connections.Verify(x => x.RemoveConnectionAsync(ClientRefId, WebSocketCloseStatus.PolicyViolation, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_rotated_secret_refuses_older_tokens_and_disconnects()
    {
        var connections = new Mock<IConnectionManager>();
        var cache = new Mock<IRevocationCache>();
        var handler = new ClientSecretRotatedHandler(connections.Object, cache.Object, NullLogger<ClientSecretRotatedHandler>.Instance);
        var rotatedAt = DateTime.UtcNow;

        await handler.Consume(Context(new ClientSecretRotatedEvent(ClientRefId, 42, 7, rotatedAt)));

        cache.Verify(x => x.RevokeIssuedBefore(ClientRefId, rotatedAt), Times.Once);
        cache.Verify(x => x.Revoke(It.IsAny<Guid>()), Times.Never);
        connections.Verify(x => x.RemoveConnectionAsync(ClientRefId, WebSocketCloseStatus.PolicyViolation, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var ctx = new Mock<ConsumeContext<T>>();
        ctx.SetupGet(x => x.Message).Returns(message);
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }
}
