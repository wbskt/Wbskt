using System.Net.WebSockets;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Handlers;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Tests.SocketHost;

public sealed class ClientStatusChangedHandlerTests
{
    private static readonly Guid ClientRefId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static (ClientStatusChangedHandler Handler, Mock<IConnectionManager> Connections, Mock<IRevocationCache> Cache) CreateHandler()
    {
        var connections = new Mock<IConnectionManager>();
        var cache = new Mock<IRevocationCache>();
        var handler = new ClientStatusChangedHandler(connections.Object, cache.Object, NullLogger<ClientStatusChangedHandler>.Instance);
        return (handler, connections, cache);
    }

    private static ConsumeContext<ClientStatusChangedEvent> Context(byte status)
    {
        var ctx = new Mock<ConsumeContext<ClientStatusChangedEvent>>();
        ctx.SetupGet(x => x.Message).Returns(new ClientStatusChangedEvent(ClientRefId, 42, Guid.NewGuid(), 5, 7, status));
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Fact]
    public async Task Revoked_status_closes_the_connection_and_denies_reconnects()
    {
        var (handler, connections, cache) = CreateHandler();

        await handler.Consume(Context(status: 2)); // Revoked

        cache.Verify(x => x.Revoke(ClientRefId), Times.Once);
        connections.Verify(x => x.RemoveConnectionAsync(ClientRefId, WebSocketCloseStatus.PolicyViolation, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Registered_status_clears_the_deny_list_and_keeps_the_connection()
    {
        var (handler, connections, cache) = CreateHandler();

        await handler.Consume(Context(status: 1)); // Registered

        cache.Verify(x => x.Clear(ClientRefId), Times.Once);
        connections.Verify(x => x.RemoveConnectionAsync(It.IsAny<Guid>(), It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
