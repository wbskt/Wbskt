using System.Linq;
using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Controllers;

namespace Wbskt.Workflow.Engine.Host.Tests.Controllers;

public sealed class InboundSignalControllerTests
{
    [Fact]
    public async Task Post_signal_routes_to_hub_with_run_scoped_correlation()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, 42, 11, "ok"));
        var controller = new InboundSignalController(hub.Object);
        Guid scopeRunRefId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post(scopeRunRefId, "approve", payload, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "signal"
                && e.MatchKeys.Contains($"signal:approve:{scopeRunRefId}")
                && e.Payload["signalName"].GetString() == "approve"
                && e.Payload["scopeRunRefId"].GetString() == scopeRunRefId.ToString()
                && e.Payload["body"].GetProperty("value").GetInt32() == 1),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_signal_reports_matched_when_bookmark_resumed()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, 42, 11, "ok"));
        var controller = new InboundSignalController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundSignalResponse response = await controller.Post(Guid.NewGuid(), "approve", payload, CancellationToken.None);

        Assert.Equal("ResumedBookmark", response.Outcome);
        Assert.True(response.Matched);
    }
}
