using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        var controller = CreateController(hub.Object, runProvider.Object);
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
        var controller = CreateController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundWebhookResponse response = (await controller.Post(Guid.NewGuid(), "alerts", payload, CancellationToken.None)).Value!;

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
        var controller = CreateController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        InboundWebhookResponse response = (await controller.Post(Guid.NewGuid(), "alerts", payload, CancellationToken.None)).Value!;

        Assert.Equal(firstRef, response.RunId);
        Assert.Equal(
            [(1L, workflowA, "StartedRun", (Guid?)firstRef), (2L, workflowB, "StartedRun", secondRef), (3L, workflowB, "Queued", null)],
            response.Registrations.Select(r => (r.RegistrationId, r.WorkflowRefId, r.Outcome, r.RunRefId)));
    }

    [Fact]
    public async Task Post_webhook_forwards_the_presented_secret_beside_the_payload()
    {
        // The secret must reach the dispatcher without ever entering the payload, which is persisted
        // as the run's trigger data and rendered in its history.
        var hub = new Mock<IInboundHub>();
        var runProvider = new Mock<IRunProvider>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "ok"));
        var controller = CreateController(hub.Object, runProvider.Object);
        controller.ControllerContext.HttpContext.Request.Headers[InboundWebhookController.SecretHeader] = "s3cret";
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post(Guid.NewGuid(), "alerts", payload, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.Secret == "s3cret" && !e.Payload.ContainsKey("secret")),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Post_webhook_reports_no_secret_when_the_caller_sent_none()
    {
        var hub = new Mock<IInboundHub>();
        var runProvider = new Mock<IRunProvider>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "ok"));
        var controller = CreateController(hub.Object, runProvider.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        await controller.Post(Guid.NewGuid(), "alerts", payload, CancellationToken.None);

        hub.Verify(h => h.HandleAsync(It.Is<InboundEvent>(e => e.Secret == null), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task The_same_idempotency_key_names_the_same_event_and_a_different_secret_does_not()
    {
        // The dispatcher dedupes on the event id, so a sender's retry must produce the same id. A
        // caller without the secret must not be able to claim it ahead of the real sender.
        var ids = new List<string>();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .Callback<InboundEvent, CancellationToken>((e, _) => ids.Add(e.InboundEventId))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "ok"));
        var workspaceRef = Guid.NewGuid();
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 1 });

        async Task SendAsync(string? key, string? secret, string path = "alerts")
        {
            var controller = CreateController(hub.Object, Mock.Of<IRunProvider>());
            if (key is not null) controller.ControllerContext.HttpContext.Request.Headers[InboundWebhookController.IdempotencyKeyHeader] = key;
            if (secret is not null) controller.ControllerContext.HttpContext.Request.Headers[InboundWebhookController.SecretHeader] = secret;
            await controller.Post(workspaceRef, path, payload, CancellationToken.None);
        }

        await SendAsync("delivery-1", "s3cret");
        await SendAsync("delivery-1", "s3cret");
        await SendAsync("delivery-1", "guess");
        await SendAsync("delivery-1", "s3cret", path: "other");
        await SendAsync("delivery-2", "s3cret");
        await SendAsync(null, "s3cret");
        await SendAsync(null, "s3cret");

        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(7, ids.Count);
        Assert.Equal(6, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id.Length + "inbound-event:".Length <= 200));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    public async Task A_malformed_idempotency_key_is_rejected_before_dispatch(string key)
    {
        var hub = new Mock<IInboundHub>();
        var controller = CreateController(hub.Object, Mock.Of<IRunProvider>());
        controller.ControllerContext.HttpContext.Request.Headers[InboundWebhookController.IdempotencyKeyHeader] = key;

        var result = await controller.Post(Guid.NewGuid(), "alerts", JsonSerializer.SerializeToElement(new { }), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        hub.Verify(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static InboundWebhookController CreateController(IInboundHub hub, IRunProvider runProvider)
    {
        return new InboundWebhookController(hub, runProvider)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
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
