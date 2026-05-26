using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Controllers;

namespace Wbskt.Workflow.Engine.Host.Tests.Controllers;

public sealed class InboundSignalControllerTests
{
    [Fact]
    public async Task Post_signal_routes_to_hub_with_signal_channel()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, 42, 11, "ok"));
        var controller = new InboundSignalController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post("corr-1", payload, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "signal"
                && e.CorrelationKey == "corr-1"
                && e.InboundEventId.StartsWith("signal:corr-1:", StringComparison.Ordinal)
                && e.Payload["body"].GetProperty("value").GetInt32() == 1),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_signal_uses_provided_correlation_key()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, 42, 11, "ok"));
        var controller = new InboundSignalController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundSignalResponse response = await controller.Post("corr-1", payload, CancellationToken.None);

        Assert.Equal("ResumedBookmark", response.Outcome);
        Assert.True(response.Matched);
    }
}
