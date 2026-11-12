using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Clients;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class ClientPresenceHandlerTests
{
    private readonly Mock<IClientProvider> _clients = new();

    [Fact]
    public async Task A_disconnect_a_host_saw_is_recorded()
    {
        var at = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

        await Handler().Consume(Context(new ClientDisconnectedEvent(Guid.NewGuid(), 4, 9, "closed", "host-a") { CreatedAtUtc = at }));

        _clients.Verify(c => c.UpdatePresenceAsync(4, false, at, "host-a", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_sweeps_own_expiry_is_not_recorded_again()
    {
        await Handler().Consume(Context(new ClientDisconnectedEvent(Guid.NewGuid(), 4, 9, "silent host", "host-a", IsExpiry: true)));

        _clients.Verify(c => c.UpdatePresenceAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private ClientPresenceHandler Handler() => new(_clients.Object, new ClientCommandQueue(
        Mock.Of<IQueuedCommandStore>(), Mock.Of<IClientCommandTargetProvider>(), Mock.Of<IEventBus>(),
        Options.Create(new CommandQueueOptions()), TimeProvider.System, NullLogger<ClientCommandQueue>.Instance));

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        return context.Object;
    }
}
