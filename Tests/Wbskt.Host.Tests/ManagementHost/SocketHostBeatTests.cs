using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Clients;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// #960: commands for a silent host's clients are queued, and its clients stay connected, so the beat that
/// shows the host alive again has to send them.
/// </summary>
public sealed class SocketHostBeatTests
{
    [Fact]
    public async Task A_beat_ending_a_silence_sends_what_queued_for_the_hosts_clients()
    {
        var hosts = new Mock<ISocketHostProvider>();
        hosts.Setup(h => h.RecordHeartbeatAsync("socket-a", new CommandQueueOptions().HostSilentAfterSeconds, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SilentHostClient(4, Guid.NewGuid(), 9), new SilentHostClient(5, Guid.NewGuid(), 9)]);
        var store = new Mock<IQueuedCommandStore>();
        store.Setup(s => s.TakeAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Beat(hosts.Object, store.Object).RecordAsync("socket-a", CancellationToken.None);

        store.Verify(s => s.TakeAsync(4, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.TakeAsync(5, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task One_client_that_cannot_be_sent_to_does_not_hold_up_the_rest()
    {
        var hosts = new Mock<ISocketHostProvider>();
        hosts.Setup(h => h.RecordHeartbeatAsync("socket-a", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SilentHostClient(4, Guid.NewGuid(), 9), new SilentHostClient(5, Guid.NewGuid(), 9)]);
        var store = new Mock<IQueuedCommandStore>();
        store.Setup(s => s.TakeAsync(4, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("database busy"));
        store.Setup(s => s.TakeAsync(5, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Beat(hosts.Object, store.Object).RecordAsync("socket-a", CancellationToken.None);

        store.Verify(s => s.TakeAsync(5, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_ordinary_beat_sends_nothing()
    {
        var hosts = new Mock<ISocketHostProvider>();
        hosts.Setup(h => h.RecordHeartbeatAsync("socket-a", It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var store = new Mock<IQueuedCommandStore>(MockBehavior.Strict);

        await Beat(hosts.Object, store.Object).RecordAsync("socket-a", CancellationToken.None);

        hosts.Verify(h => h.RecordHeartbeatAsync("socket-a", It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    internal static ISocketHostProvider QuietHosts()
    {
        var hosts = new Mock<ISocketHostProvider>();
        hosts.Setup(h => h.RecordHeartbeatAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return hosts.Object;
    }

    internal static SocketHostBeat Beat(ISocketHostProvider hosts, IQueuedCommandStore store)
    {
        var options = Options.Create(new CommandQueueOptions());
        var queue = new ClientCommandQueue(store, Mock.Of<IClientCommandTargetProvider>(), Mock.Of<IEventBus>(), options, TimeProvider.System, NullLogger<ClientCommandQueue>.Instance);
        return new SocketHostBeat(hosts, queue, options, NullLogger<SocketHostBeat>.Instance);
    }
}
