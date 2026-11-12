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

public sealed class SocketHostPresenceSnapshotHandlerTests
{
    [Fact]
    public async Task Clients_the_live_host_no_longer_holds_are_cleared_and_announced()
    {
        var held = Guid.NewGuid();
        var takenAt = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var lost = new PresenceRepair(4, Guid.NewGuid(), 9, Connected: false, takenAt);
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.ReconcilePresenceByHostAsync("socket-a", It.Is<IReadOnlyCollection<Guid>>(r => r.Single() == held), takenAt,
                SocketHostPresenceSnapshotHandler.Margin, It.IsAny<CancellationToken>()))
            .ReturnsAsync([lost]);
        var bus = new Mock<IEventBus>();
        var context = new Mock<ConsumeContext<SocketHostPresenceSnapshot>>();
        context.SetupGet(c => c.Message).Returns(new SocketHostPresenceSnapshot("socket-a", [held], takenAt));

        await new SocketHostPresenceSnapshotHandler(provider.Object, bus.Object, NullLogger<SocketHostPresenceSnapshotHandler>.Instance).Consume(context.Object);

        bus.Verify(b => b.PublishAsync(
            It.Is<ClientDisconnectedEvent>(e => e.ClientRefId == lost.ClientRefId && e.ClientId == 4 && e.WorkspaceId == 9 && e.HostId == "socket-a"
                && e.Reason == SocketHostPresenceSnapshotHandler.Reason && e.CreatedAtUtc == takenAt),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Clients_the_live_host_still_holds_after_they_expired_are_announced_connected_again()
    {
        var takenAt = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        // Dated just after the expiry the database recorded, not at the snapshot.
        var restoredAt = takenAt.AddMinutes(-3);
        var restored = new PresenceRepair(5, Guid.NewGuid(), 9, Connected: true, restoredAt);
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.ReconcilePresenceByHostAsync("socket-a", It.IsAny<IReadOnlyCollection<Guid>>(), takenAt,
                SocketHostPresenceSnapshotHandler.Margin, It.IsAny<CancellationToken>()))
            .ReturnsAsync([restored]);
        var bus = new Mock<IEventBus>();
        var context = new Mock<ConsumeContext<SocketHostPresenceSnapshot>>();
        context.SetupGet(c => c.Message).Returns(new SocketHostPresenceSnapshot("socket-a", [restored.ClientRefId], takenAt));

        await new SocketHostPresenceSnapshotHandler(provider.Object, bus.Object, NullLogger<SocketHostPresenceSnapshotHandler>.Instance).Consume(context.Object);

        bus.Verify(b => b.PublishAsync(
            It.Is<ClientConnectedEvent>(e => e.ClientRefId == restored.ClientRefId && e.ClientId == 5 && e.WorkspaceId == 9 && e.HostId == "socket-a" && e.IsRestored && e.CreatedAtUtc == restoredAt),
            It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(b => b.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
