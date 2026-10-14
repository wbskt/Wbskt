using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class StalePresenceSweeperTests
{
    [Fact]
    public async Task Each_client_of_a_silent_host_is_announced_as_disconnected()
    {
        var first = new ExpiredPresence(1, Guid.NewGuid(), 7, "old-container");
        var second = new ExpiredPresence(2, Guid.NewGuid(), 8, "old-container");
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.ExpirePresenceAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        var bus = new Mock<IEventBus>();

        var count = await CreateSweeper(provider.Object, bus.Object, new PresenceOptions()).SweepAsync(CancellationToken.None);

        Assert.Equal(2, count);
        bus.Verify(b => b.PublishAsync(
            It.Is<ClientDisconnectedEvent>(e => e.ClientRefId == first.ClientRefId && e.ClientId == 1 && e.WorkspaceId == 7 && e.HostId == "old-container"),
            It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(b => b.PublishAsync(It.Is<ClientDisconnectedEvent>(e => e.ClientRefId == second.ClientRefId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_host_counts_as_gone_after_the_configured_silence()
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.ExpirePresenceAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await CreateSweeper(provider.Object, Mock.Of<IEventBus>(), new PresenceOptions { HostStaleAfterSeconds = 300 }).SweepAsync(CancellationToken.None);

        provider.Verify(p => p.ExpirePresenceAsync(TimeSpan.FromSeconds(300), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_failed_announcement_does_not_stop_the_others()
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.ExpirePresenceAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ExpiredPresence(1, Guid.NewGuid(), 7, "h"), new ExpiredPresence(2, Guid.NewGuid(), 7, "h")]);
        var bus = new Mock<IEventBus>();
        bus.SetupSequence(b => b.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"))
            .Returns(Task.CompletedTask);

        var count = await CreateSweeper(provider.Object, bus.Object, new PresenceOptions()).SweepAsync(CancellationToken.None);

        Assert.Equal(2, count);
        bus.Verify(b => b.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static StalePresenceSweeper CreateSweeper(IClientProvider provider, IEventBus bus, PresenceOptions options)
    {
        var services = new ServiceCollection().AddSingleton(provider).AddSingleton(bus).BuildServiceProvider();
        return new StalePresenceSweeper(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(options), NullLogger<StalePresenceSweeper>.Instance);
    }
}
