using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Controllers;

namespace Wbskt.Workflow.Engine.Host.Tests.Controllers;

public sealed class InboundManualControllerTests
{
    private static RunRow MakeRunRow(int id, Guid refId, Guid workflowRefId)
    {
        return new RunRow
        {
            Id = id,
            RefId = refId,
            WorkflowDefinitionId = 9,
            WorkflowRefId = workflowRefId,
            WorkflowVersion = 1,
            TriggerNodeId = Guid.NewGuid(),
            CorrelationKey = $"manual:{workflowRefId}",
            Status = "Running",
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 10m,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task Post_sends_inbound_event_with_manual_channel_kind()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok"));
        var runProvider = new Mock<IRunProvider>();
        runProvider.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRunRow(42, runRefId, workflowRefId));
        var controller = new InboundManualController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.ChannelKind == "manual"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_sends_inbound_event_with_correct_correlation_key()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok"));
        var runProvider = new Mock<IRunProvider>();
        runProvider.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRunRow(42, runRefId, workflowRefId));
        var controller = new InboundManualController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.MatchKeys.Contains($"manual:{workflowRefId}")),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_includes_workflowDefinitionRefId_in_payload()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok"));
        var runProvider = new Mock<IRunProvider>();
        runProvider.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRunRow(42, runRefId, workflowRefId));
        var controller = new InboundManualController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.Payload.ContainsKey("workflowDefinitionRefId")
                && e.Payload["workflowDefinitionRefId"].GetString() == workflowRefId.ToString()),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_returns_run_ref_id_and_run_id_when_run_started()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok"));
        var runProvider = new Mock<IRunProvider>();
        runProvider.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRunRow(42, runRefId, workflowRefId));
        var controller = new InboundManualController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundManualResponse response = await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        Assert.Equal("StartedRun", response.Outcome);
        Assert.Equal(runRefId, response.RunRefId);
        Assert.Equal(42, response.RunId);
    }

    [Fact]
    public async Task Post_returns_null_refs_when_no_run_started()
    {
        var workflowRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "no-reg"));
        var runProvider = new Mock<IRunProvider>();
        var controller = new InboundManualController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { });

        InboundManualResponse response = await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        Assert.Equal("NoRegistration", response.Outcome);
        Assert.Null(response.RunRefId);
        Assert.Null(response.RunId);
        runProvider.Verify(r => r.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Post_reports_every_registration_the_event_matched()
    {
        // A workflow can carry more than one manual trigger; the response must name every run it
        // started, not just whichever one the summary happens to point at.
        var workflowRefId = Guid.NewGuid();
        var firstRunRefId = Guid.NewGuid();
        var secondRunRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok",
            [
                new TriggerRegistrationDispatch(7, workflowRefId, TriggerDispatchOutcome.StartedRun, 42, "ok"),
                new TriggerRegistrationDispatch(8, workflowRefId, TriggerDispatchOutcome.StartedRun, 43, "ok"),
                new TriggerRegistrationDispatch(9, workflowRefId, TriggerDispatchOutcome.Dropped, null, "ok")
            ]));
        var runProvider = new Mock<IRunProvider>();
        runProvider.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRunRow(42, firstRunRefId, workflowRefId));
        runProvider.Setup(r => r.GetByIdAsync(43, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRunRow(43, secondRunRefId, workflowRefId));
        var controller = new InboundManualController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundManualResponse response = await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        Assert.Equal(firstRunRefId, response.RunRefId);
        Assert.Equal(
            [(7L, "StartedRun", (Guid?)firstRunRefId), (8L, "StartedRun", secondRunRefId), (9L, "Dropped", null)],
            response.Registrations.Select(r => (r.RegistrationId, r.Outcome, r.RunRefId)));
        // The summary run was resolved from the projection, not looked up a second time.
        runProvider.Verify(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_with_idempotency_key_uses_it_as_the_inbound_event_id()
    {
        var workflowRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, null, null, "ok"));
        var runProvider = new Mock<IRunProvider>();
        var controller = new InboundManualController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post(workflowRefId, payload, "order-123", CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.InboundEventId == $"manual:{workflowRefId}:order-123"),
            CancellationToken.None), Times.Once);
    }
}

