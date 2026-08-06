using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Host.Controllers;
using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Engine.Host.Tests.Controllers;

public sealed class InboundWebhookControllerTests
{
    [Fact]
    public async Task Post_webhook_routes_to_hub_with_correct_channel_kind()
    {
        var hub = new Mock<IInboundHub>();
        var runProvider = new Mock<IRunProvider>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok"));
        runProvider.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunRow { Id = 42, RefId = Guid.Empty, WorkflowDefinitionId = 1, WorkflowRefId = Guid.Empty, WorkflowVersion = 1, TriggerNodeId = Guid.Empty, CorrelationKey = null, Status = "Active", StartedAt = DateTime.UtcNow, CompletedAt = null, CancellationRequestedAt = null, CancellationReason = null, CreditBudget = 0, CreatedAt = DateTime.UtcNow });
        var controller = new InboundWebhookController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });
        var workspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");

        await controller.Post(workspaceRef, "alerts", payload, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "webhook"
                && e.MatchKeys.Contains($"webhook:{workspaceRef}:alerts")
                && e.InboundEventId.StartsWith($"webhook:{workspaceRef}:alerts:", StringComparison.Ordinal)
                && e.Payload["workspaceRefId"].GetString() == workspaceRef.ToString()
                && e.Payload["body"].GetProperty("value").GetInt32() == 1),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_webhook_returns_dispatch_outcome()
    {
        var hub = new Mock<IInboundHub>();
        var runProvider = new Mock<IRunProvider>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok"));
        var testRef = Guid.NewGuid();
        runProvider.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunRow { Id = 42, RefId = testRef, WorkflowDefinitionId = 1, WorkflowRefId = Guid.Empty, WorkflowVersion = 1, TriggerNodeId = Guid.Empty, CorrelationKey = null, Status = "Active", StartedAt = DateTime.UtcNow, CompletedAt = null, CancellationRequestedAt = null, CancellationReason = null, CreditBudget = 0, CreatedAt = DateTime.UtcNow });
        var controller = new InboundWebhookController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundWebhookResponse response = await controller.Post(Guid.NewGuid(), "alerts", payload, CancellationToken.None);

        Assert.Equal("StartedRun", response.Outcome);
        Assert.Equal(testRef, response.RunId);
    }

    [Fact]
    public async Task Post_webhook_reports_every_registration_the_path_matched()
    {
        // Two workflows in a workspace may share a webhook path, so one call can start two runs and
        // drop a third. The single RunId can only name one of them.
        var firstRef = Guid.NewGuid();
        var secondRef = Guid.NewGuid();
        var workflowA = Guid.NewGuid();
        var workflowB = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        var runProvider = new Mock<IRunProvider>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok",
            [
                new TriggerRegistrationDispatch(1, workflowA, TriggerDispatchOutcome.StartedRun, 42, "ok"),
                new TriggerRegistrationDispatch(2, workflowB, TriggerDispatchOutcome.StartedRun, 43, "ok"),
                new TriggerRegistrationDispatch(3, workflowB, TriggerDispatchOutcome.Queued, null, "ok")
            ]));
        runProvider.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(MakeRunRow(42, firstRef));
        runProvider.Setup(r => r.GetByIdAsync(43, It.IsAny<CancellationToken>())).ReturnsAsync(MakeRunRow(43, secondRef));
        var controller = new InboundWebhookController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundWebhookResponse response = await controller.Post(Guid.NewGuid(), "alerts", payload, CancellationToken.None);

        Assert.Equal(firstRef, response.RunId);
        Assert.Equal(
            [(1L, workflowA, "StartedRun", (Guid?)firstRef), (2L, workflowB, "StartedRun", secondRef), (3L, workflowB, "Queued", null)],
            response.Registrations.Select(r => (r.RegistrationId, r.WorkflowRefId, r.Outcome, r.RunRefId)));
    }

    private static RunRow MakeRunRow(int id, Guid refId)
    {
        return new RunRow
        {
            Id = id,
            RefId = refId,
            WorkflowDefinitionId = 1,
            WorkflowRefId = Guid.Empty,
            WorkflowVersion = 1,
            TriggerNodeId = Guid.Empty,
            CorrelationKey = null,
            Status = "Active",
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 0,
            CreatedAt = DateTime.UtcNow
        };
    }
}
