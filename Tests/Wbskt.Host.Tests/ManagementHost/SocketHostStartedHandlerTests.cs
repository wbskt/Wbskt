using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Events.Hosting;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class SocketHostStartedHandlerTests
{
    [Fact]
    public async Task Clears_only_the_restarted_host_and_announces_each_client_as_disconnected()
    {
        var first = new ExpiredPresence(1, Guid.NewGuid(), 7, "socket-1");
        var second = new ExpiredPresence(2, Guid.NewGuid(), 8, "socket-1");
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.ResetPresenceByHostAsync("socket-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        var bus = new Mock<IEventBus>();

        await CreateHandler(provider.Object, bus.Object).Consume(Context("socket-1"));

        provider.Verify(p => p.ResetPresenceByHostAsync("socket-1", It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(b => b.PublishAsync(
            It.Is<ClientDisconnectedEvent>(e => e.ClientRefId == first.ClientRefId && e.ClientId == 1 && e.WorkspaceId == 7 && e.HostId == "socket-1" && e.Reason == SocketHostStartedHandler.Reason),
            It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(b => b.PublishAsync(It.Is<ClientDisconnectedEvent>(e => e.ClientRefId == second.ClientRefId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_host_on_a_fresh_id_announces_nothing()
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.ResetPresenceByHostAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var bus = new Mock<IEventBus>();

        await CreateHandler(provider.Object, bus.Object).Consume(Context("socket-new"));

        bus.Verify(b => b.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_failed_announcement_does_not_stop_the_others()
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.ResetPresenceByHostAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ExpiredPresence(1, Guid.NewGuid(), 7, "h"), new ExpiredPresence(2, Guid.NewGuid(), 7, "h")]);
        var bus = new Mock<IEventBus>();
        bus.SetupSequence(b => b.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"))
            .Returns(Task.CompletedTask);

        await CreateHandler(provider.Object, bus.Object).Consume(Context("h"));

        bus.Verify(b => b.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static SocketHostStartedHandler CreateHandler(IClientProvider provider, IEventBus bus)
    {
        return new SocketHostStartedHandler(provider, bus, NullLogger<SocketHostStartedHandler>.Instance);
    }

    private static ConsumeContext<SocketHostStartedEvent> Context(string hostId)
    {
        var ctx = new Mock<ConsumeContext<SocketHostStartedEvent>>();
        ctx.SetupGet(x => x.Message).Returns(new SocketHostStartedEvent(hostId));
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }
}
