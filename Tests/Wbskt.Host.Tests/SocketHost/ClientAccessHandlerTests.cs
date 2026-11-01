using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Handlers;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Host.Tests.SocketHost;

public sealed class ClientAccessHandlerTests
{
    private static readonly Guid ClientRefId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task A_deleted_client_is_revoked()
    {
        var gate = new Mock<IClientAccessGate>();

        await new ClientDeletedHandler(gate.Object, NullLogger<ClientDeletedHandler>.Instance)
            .Consume(Context(new ClientDeletedEvent(ClientRefId, 42, Guid.NewGuid(), 5, 7, "device")));

        gate.Verify(x => x.RevokeAsync(ClientRefId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_rotated_secret_refuses_older_tokens_only()
    {
        var gate = new Mock<IClientAccessGate>();
        var rotatedAt = DateTime.UtcNow;

        await new ClientSecretRotatedHandler(gate.Object, NullLogger<ClientSecretRotatedHandler>.Instance)
            .Consume(Context(new ClientSecretRotatedEvent(ClientRefId, 42, 7, rotatedAt)));

        gate.Verify(x => x.RevokeIssuedBeforeAsync(ClientRefId, rotatedAt, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        gate.Verify(x => x.RevokeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Leaving_registered_revokes()
    {
        var gate = new Mock<IClientAccessGate>();

        await new ClientStatusChangedHandler(gate.Object, NullLogger<ClientStatusChangedHandler>.Instance)
            .Consume(Context(new ClientStatusChangedEvent(ClientRefId, 42, Guid.NewGuid(), 5, 7, 2)));

        gate.Verify(x => x.RevokeAsync(ClientRefId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approval_reinstates_from_the_events_moment_and_closes_nothing()
    {
        var gate = new Mock<IClientAccessGate>();
        var approvedAt = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

        await new ClientStatusChangedHandler(gate.Object, NullLogger<ClientStatusChangedHandler>.Instance)
            .Consume(Context(new ClientStatusChangedEvent(ClientRefId, 42, Guid.NewGuid(), 5, 7, ClientStatusChangedEvent.RegisteredStatus) { CreatedAtUtc = approvedAt }));

        gate.Verify(x => x.Reinstate(ClientRefId, approvedAt), Times.Once);
        gate.Verify(x => x.RevokeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void The_shared_registered_status_matches_the_management_enum()
    {
        Assert.Equal((byte)Wbskt.Protocol.ClientStatus.Registered, ClientStatusChangedEvent.RegisteredStatus);
    }

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var ctx = new Mock<ConsumeContext<T>>();
        ctx.SetupGet(x => x.Message).Returns(message);
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }
}
