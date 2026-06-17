using System.Linq;
using MassTransit;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class ClientPayloadReceivedConsumerTests
{
    [Fact]
    public async Task Consume_invokes_inbound_hub_with_device_channel_and_device_correlation_key()
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
                e.ChannelKind == "device"
                && e.MatchKeys.Contains($"device:{evt.ClientRefId}:{evt.MessageType}")
                && e.InboundEventId.StartsWith($"client-payload:{evt.ClientRefId}:", StringComparison.Ordinal)
                && e.Payload["deviceSerial"].GetString() == evt.ClientRefId.ToString()
                && e.Payload["payloadType"].GetString() == evt.MessageType
                && e.Payload["clientRefId"].GetGuid() == evt.ClientRefId
                && e.Payload["clientId"].GetInt32() == evt.ClientId
                && e.Payload["workspaceId"].GetInt32() == evt.WorkspaceId
                && e.Payload["payload"].GetRawText() == evt.Payload),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_uses_device_ref_id_and_message_type_as_correlation_key()
    {
        // Arrange
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        ClientPayloadReceivedEvent evt = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 12, 34, "temperature", "{}");
        var context = new Mock<ConsumeContext<ClientPayloadReceivedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new ClientPayloadReceivedConsumer(hub.Object);

        // Act
        await consumer.Consume(context.Object);

        // Assert
        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "device"
                && e.MatchKeys.Contains("device:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb:temperature")),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_includes_clientId_and_workspaceId_in_payload()
    {
        // Arrange
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        ClientPayloadReceivedEvent evt = new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 99, 77, "humidity", "{}");
        var context = new Mock<ConsumeContext<ClientPayloadReceivedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new ClientPayloadReceivedConsumer(hub.Object);

        // Act
        await consumer.Consume(context.Object);

        // Assert
        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.Payload["clientId"].GetInt32() == 99
                && e.Payload["workspaceId"].GetInt32() == 77),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_uses_bus_message_id_so_redelivery_is_deduplicated()
    {
        // Two deliveries of the SAME message (same MessageId) must produce the SAME
        // InboundEventId, so the downstream idempotency claim dedupes the redelivery.
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        ClientPayloadReceivedEvent evt = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 12, 34, "sensor", "{}");
        Guid messageId = Guid.Parse("11112222-3333-4444-5555-666677778888");

        var capturedIds = new List<string>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .Callback<InboundEvent, CancellationToken>((e, _) => capturedIds.Add(e.InboundEventId))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));

        var consumer = new ClientPayloadReceivedConsumer(hub.Object);

        await consumer.Consume(ContextWithMessageId(evt, messageId).Object);
        await consumer.Consume(ContextWithMessageId(evt, messageId).Object);

        Assert.Equal(2, capturedIds.Count);
        Assert.Equal(capturedIds[0], capturedIds[1]);
        Assert.EndsWith(messageId.ToString(), capturedIds[0]);
    }

    private static Mock<ConsumeContext<ClientPayloadReceivedEvent>> ContextWithMessageId(ClientPayloadReceivedEvent evt, Guid messageId)
    {
        var context = new Mock<ConsumeContext<ClientPayloadReceivedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.MessageId).Returns(messageId);
        return context;
    }
}
