using MassTransit;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class ClientPayloadReceivedConsumerTests
{
    [Fact]
    public async Task Consume_invokes_inbound_hub_with_client_payload_channel()
    {
        // Arrange
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        ClientPayloadReceivedEvent evt = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 12, 34, "sensor", "{\"value\":1}");
        var context = new Mock<ConsumeContext<ClientPayloadReceivedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new ClientPayloadReceivedConsumer(hub.Object);

        // Act
        await consumer.Consume(context.Object);

        // Assert
        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "client-payload"
                && e.CorrelationKey == $"client:{evt.ClientRefId}"
                && e.InboundEventId.StartsWith($"client-payload:{evt.ClientRefId}:", StringComparison.Ordinal)
                && e.Payload["clientRefId"].GetGuid() == evt.ClientRefId
                && e.Payload["messageType"].GetString() == evt.MessageType
                && e.Payload["payload"].GetString() == evt.Payload),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_uses_client_ref_id_as_correlation_key()
    {
        // Arrange
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        ClientPayloadReceivedEvent evt = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 12, 34, "sensor", "{}");
        var context = new Mock<ConsumeContext<ClientPayloadReceivedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new ClientPayloadReceivedConsumer(hub.Object);

        // Act
        await consumer.Consume(context.Object);

        // Assert
        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.CorrelationKey == "client:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CancellationToken.None), Times.Once);
    }
}
