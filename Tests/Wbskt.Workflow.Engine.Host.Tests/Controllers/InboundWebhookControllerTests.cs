using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Controllers;

namespace Wbskt.Workflow.Engine.Host.Tests.Controllers;

public sealed class InboundWebhookControllerTests
{
    [Fact]
    public async Task Post_webhook_routes_to_hub_with_correct_channel_kind()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok"));
        var controller = new InboundWebhookController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post("alerts", payload, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "alerts"
                && e.CorrelationKey == "webhook:alerts"
                && e.InboundEventId.StartsWith("webhook:alerts:", StringComparison.Ordinal)
                && e.Payload["body"].GetProperty("value").GetInt32() == 1),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_webhook_returns_dispatch_outcome()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok"));
        var controller = new InboundWebhookController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundWebhookResponse response = await controller.Post("alerts", payload, CancellationToken.None);

        Assert.Equal("StartedRun", response.Outcome);
        Assert.Equal(42, response.RunId);
    }
}
