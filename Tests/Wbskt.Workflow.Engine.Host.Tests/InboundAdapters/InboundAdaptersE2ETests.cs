using System.Text.Json;
using MassTransit;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Controllers;
using Wbskt.Workflow.Engine.Host.InboundAdapters;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class InboundAdaptersE2ETests
{
    [Fact]
    public async Task ClientPayloadConsumer_routes_to_hub()
    {
        var hub = CreateHub();
        var consumer = new ClientPayloadReceivedConsumer(hub.Object);
        var context = new Mock<ConsumeContext<ClientPayloadReceivedEvent>>();
        ClientPayloadReceivedEvent evt = new(Guid.NewGuid(), 1, 1, "sensor", "{}");
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "device"
                && e.CorrelationKey == $"device:{evt.ClientRefId}:{evt.MessageType}"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task WebhookController_routes_to_hub()
    {
        var hub = CreateHub();
        var controller = new InboundWebhookController(hub.Object);

        await controller.Post("alerts", JsonSerializer.SerializeToElement(new { value = 1 }), CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.ChannelKind == "alerts"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task SignalController_routes_to_hub()
    {
        var hub = CreateHub();
        var controller = new InboundSignalController(hub.Object);
        Guid scopeRunRefId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        await controller.Post(scopeRunRefId, "approve", JsonSerializer.SerializeToElement(new { value = 1 }), CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.ChannelKind == "signal" && e.CorrelationKey == $"signal:approve:{scopeRunRefId}"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task SubWorkflowHook_emits_child_completed_via_hub()
    {
        var hub = CreateHub();
        var hook = new SubWorkflowCompletionHook(hub.Object, new FixedIdGenerator());
        Guid runRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        await hook.OnRunCompletedAsync(runRefId, "Succeeded", CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "child-completed"
                && e.CorrelationKey == $"child-completed:{runRefId}"),
            CancellationToken.None), Times.Once);
    }

    private static Mock<IInboundHub> CreateHub()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        return hub;
    }

    private sealed class FixedIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.Parse("11111111-1111-1111-1111-111111111111");
    }
}
