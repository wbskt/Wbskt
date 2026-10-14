using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Engine.Host.Controllers;

namespace Wbskt.Host.Tests.Controllers;

public sealed class InboundManualControllerTests
{
    [Fact]
    public async Task Post_sends_inbound_event_with_manual_channel_kind()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok"));
        var controller = new InboundManualController(hub.Object);
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
        var controller = new InboundManualController(hub.Object);
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
        var controller = new InboundManualController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.Payload.ContainsKey("workflowDefinitionRefId")
                && e.Payload["workflowDefinitionRefId"].GetString() == workflowRefId.ToString()),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_returns_the_started_runs_ref_id_and_never_its_row_id()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 42, null, "ok", RunRefId: runRefId));
        var controller = new InboundManualController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundResponse response = await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        Assert.Equal(InboundOutcome.StartedRun, response.Outcome);
        Assert.Equal(runRefId, response.RunRefId);
        // The wire shape names the run by RefId and has no field for its row id.
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(["outcome", "runRefId", "registrations"], json.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Post_returns_null_refs_when_no_run_started()
    {
        var workflowRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "no-reg"));
        var controller = new InboundManualController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { });

        InboundResponse response = await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        Assert.Equal(InboundOutcome.NoRegistration, response.Outcome);
        Assert.Null(response.RunRefId);
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
                new TriggerRegistrationDispatch(7, workflowRefId, TriggerDispatchOutcome.StartedRun, 42, "ok", firstRunRefId),
                new TriggerRegistrationDispatch(8, workflowRefId, TriggerDispatchOutcome.StartedRun, 43, "ok", secondRunRefId),
                new TriggerRegistrationDispatch(9, workflowRefId, TriggerDispatchOutcome.Dropped, null, "ok")
            ], firstRunRefId));
        var controller = new InboundManualController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundResponse response = await controller.Post(workflowRefId, payload, null, CancellationToken.None);

        Assert.Equal(firstRunRefId, response.RunRefId);
        Assert.Equal(
            [(InboundOutcome.StartedRun, (Guid?)firstRunRefId), (InboundOutcome.StartedRun, secondRunRefId), (InboundOutcome.Dropped, null)],
            response.Registrations.Select(r => (r.Outcome, r.RunRefId)));
    }

    [Fact]
    public async Task Post_with_idempotency_key_uses_it_as_the_inbound_event_id()
    {
        var workflowRefId = Guid.NewGuid();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, null, null, "ok"));
        var controller = new InboundManualController(hub.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post(workflowRefId, payload, "order-123", CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.InboundEventId == $"manual:{workflowRefId}:order-123"),
            CancellationToken.None), Times.Once);
    }
}

