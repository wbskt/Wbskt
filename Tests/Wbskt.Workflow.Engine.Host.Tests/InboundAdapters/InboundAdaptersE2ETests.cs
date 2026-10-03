using System.Text.Json;
using MassTransit;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Controllers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.InboundAdapters;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class InboundAdaptersE2ETests
{
    [Fact]
    public async Task ClientPayloadConsumer_routes_to_hub()
    {
        var hub = CreateHub();
        var consumer = new ClientPayloadReceivedConsumer(hub.Object, RecordingHoldStateProvider.EmptyRecorder());
        var context = new Mock<ConsumeContext<ClientMessageReceivedEvent>>();
        ClientMessageReceivedEvent evt = new(Guid.NewGuid(), 1, 1, "sensor", "{}");
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "client"
                && e.MatchKeys.Contains($"client:{evt.ClientRefId}:{evt.Type}")),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task WebhookController_routes_to_hub()
    {
        var hub = CreateHub();
        var runProvider = new Mock<IRunProvider>();
        runProvider.Setup(r => r.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunRow { Id = 1, RefId = Guid.Empty, WorkflowDefinitionId = 1, WorkflowRefId = Guid.Empty, WorkflowVersion = 1, TriggerNodeId = Guid.Empty, CorrelationKey = null, Status = "Active", StartedAt = DateTime.UtcNow, CompletedAt = null, CancellationRequestedAt = null, CancellationReason = null, CreditBudget = 0, CreatedAt = DateTime.UtcNow });
        var controller = new InboundWebhookController(hub.Object, runProvider.Object)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };

        await controller.Post(Guid.NewGuid(), "alerts", JsonSerializer.SerializeToElement(new { value = 1 }), CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.ChannelKind == "webhook"),
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
            It.Is<InboundEvent>(e => e.ChannelKind == "signal" && e.MatchKeys.Contains($"signal:approve:{scopeRunRefId}")),
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
                && e.MatchKeys.Contains($"child-completed:{runRefId}")),
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
