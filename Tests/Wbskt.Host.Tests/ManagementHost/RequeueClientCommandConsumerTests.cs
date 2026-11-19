using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Host.Tests.Infrastructure;
using Wbskt.Infrastructure.Clients;
using Wbskt.Management.Host.Handlers;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// A command a socket host hands back waits for the client's next connect, unless the client lost its
/// registration meanwhile: then it ends as failed, so an approval within its expiry cannot deliver it.
/// </summary>
public sealed class RequeueClientCommandConsumerTests
{
    private const int ClientId = 42;
    private const int WorkspaceId = 7;
    private static readonly Guid ClientRefId = Guid.NewGuid();

    [Fact]
    public async Task A_command_for_a_registered_client_is_queued_again()
    {
        var (store, bus) = await ConsumeAsync(ClientStatus.Registered);

        Assert.Single(store.Rows);
        bus.Verify(b => b.PublishAsync(It.IsAny<ClientCommandFailedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ClientStatus.Revoked)]
    [InlineData(ClientStatus.Rejected)]
    [InlineData(ClientStatus.Pending)]
    public async Task A_command_for_a_client_no_longer_registered_ends_as_failed_and_is_not_queued(ClientStatus status)
    {
        var (store, bus) = await ConsumeAsync(status);

        Assert.Empty(store.Rows);
        bus.Verify(b => b.PublishAsync(
            It.Is<ClientCommandFailedEvent>(e => e.ClientRefId == ClientRefId && e.Reason == ClientCommandFailedEvent.RevokedWhileQueuedReason),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<(InMemoryQueuedCommandStore Store, Mock<IEventBus> Bus)> ConsumeAsync(ClientStatus status)
    {
        var store = new InMemoryQueuedCommandStore();
        var bus = new Mock<IEventBus>();
        var targets = new Mock<IClientCommandTargetProvider>();
        targets.Setup(t => t.FindAsync(ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientCommandTarget(ClientId, WorkspaceId, IsConnected: false, HostId: null, Status: status));
        var consumer = new RequeueClientCommandConsumer(
            TestClientCommands.Queue(store, targets.Object, bus.Object), targets.Object, bus.Object, NullLogger<RequeueClientCommandConsumer>.Instance);

        var context = new Mock<ConsumeContext<RequeueClientCommand>>();
        context.SetupGet(c => c.Message).Returns(new RequeueClientCommand(ClientRefId, ClientId, WorkspaceId, "reboot", "{}", Guid.NewGuid(),
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, ReturnedByHostId: "gw-1", ReturnedAtUtc: DateTime.UtcNow));
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);
        return (store, bus);
    }
}
