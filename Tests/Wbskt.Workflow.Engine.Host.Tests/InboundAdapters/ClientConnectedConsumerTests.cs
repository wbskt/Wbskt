using MassTransit;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class ClientConnectedConsumerTests
{
    [Fact]
    public async Task Consume_invokes_inbound_hub_with_client_connected_channel()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        ClientConnectedEvent evt = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 12, 34, "test-host");
        var context = new Mock<ConsumeContext<ClientConnectedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new ClientConnectedConsumer(hub.Object);

        await consumer.Consume(context.Object);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "client-connected"
                && e.MatchKeys.Contains($"client:{evt.ClientRefId}")
                && e.InboundEventId.StartsWith($"client-connected:{evt.ClientRefId}:", StringComparison.Ordinal)
                && e.Payload["clientRefId"].GetGuid() == evt.ClientRefId
                && e.Payload["clientId"].GetInt32() == evt.ClientId
                && e.Payload["workspaceId"].GetInt32() == evt.WorkspaceId),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_uses_client_ref_id_as_correlation_key()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        ClientConnectedEvent evt = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 12, 34, "test-host");
        var context = new Mock<ConsumeContext<ClientConnectedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new ClientConnectedConsumer(hub.Object);

        await consumer.Consume(context.Object);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.MatchKeys.Contains("client:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            CancellationToken.None), Times.Once);
    }
}
