using MassTransit;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class ClientLatencyMeasuredHandlerTests
{
    [Fact]
    public async Task Rtt_is_rounded_and_persisted_with_the_event_timestamp()
    {
        var provider = new Mock<IClientProvider>();
        var handler = new ClientLatencyMeasuredHandler(provider.Object);
        var @event = new ClientLatencyMeasuredEvent(Guid.NewGuid(), 42, 7, 41.6);

        var ctx = new Mock<ConsumeContext<ClientLatencyMeasuredEvent>>();
        ctx.SetupGet(x => x.Message).Returns(@event);
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await handler.Consume(ctx.Object);

        provider.Verify(x => x.UpdateRttAsync(42, 42, @event.CreatedAtUtc, It.IsAny<CancellationToken>()), Times.Once);
    }
}
