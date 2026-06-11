using System.Text.Json;
using System.Linq;
using Moq;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Controllers;

namespace Wbskt.Workflow.Engine.Host.Tests.Controllers;

public sealed class InboundWakeControllerTests
{
    [Fact]
    public async Task Post_wake_routes_to_hub_with_http_wake_channel_and_token()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, 42, 11, "ok"));
        var controller = new InboundWakeController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { approved = true });

        await controller.Post("dddddddd-dddd-dddd-dddd-dddddddddddd", payload, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "http-wake"
                && e.MatchKeys.Contains("http-wake:dddddddd-dddd-dddd-dddd-dddddddddddd")
                && e.Payload["wakeToken"].GetString() == "dddddddd-dddd-dddd-dddd-dddddddddddd"
                && e.Payload["body"].GetProperty("approved").GetBoolean()),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_wake_reports_matched_when_bookmark_resumed()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, 42, 11, "ok"));
        var controller = new InboundWakeController(hub.Object);

        InboundWakeResponse response = await controller.Post("token-1", JsonSerializer.SerializeToElement(new { }), CancellationToken.None);

        Assert.Equal("ResumedBookmark", response.Outcome);
        Assert.True(response.Matched);
    }
}
