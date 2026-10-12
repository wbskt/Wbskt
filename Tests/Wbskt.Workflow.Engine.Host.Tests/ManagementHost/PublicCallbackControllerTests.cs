using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Management.Host.Controllers.Workflow;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class PublicCallbackControllerTests
{
    private static readonly JsonElement Payload = JsonDocument.Parse("{\"value\":1}").RootElement;

    [Fact]
    public async Task A_webhook_is_queued_for_the_engine_with_its_secret_and_key_and_answered_202()
    {
        var engine = new Mock<IWorkflowEngineGateway>();
        var controller = CreateController(engine.Object);
        var workspaceRef = Guid.NewGuid();
        controller.Request.Headers[PublicCallbackController.WebhookSecretHeader] = "s3cret";
        controller.Request.Headers[PublicCallbackController.IdempotencyKeyHeader] = "delivery-1";

        var result = await controller.Webhook(workspaceRef, "door", Payload, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        engine.Verify(e => e.SendWebhookAsync(workspaceRef, "door", Payload, "s3cret", "delivery-1", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task A_wake_is_queued_for_the_engine_and_answered_202()
    {
        var engine = new Mock<IWorkflowEngineGateway>();

        var result = await CreateController(engine.Object).Wake("token", Payload, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        engine.Verify(e => e.SendWakeAsync("token", Payload, CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    public async Task A_malformed_idempotency_key_is_refused_before_anything_is_queued(string key)
    {
        var engine = new Mock<IWorkflowEngineGateway>(MockBehavior.Strict);
        var controller = CreateController(engine.Object);
        controller.Request.Headers[PublicCallbackController.IdempotencyKeyHeader] = key;

        var result = await controller.Webhook(Guid.NewGuid(), "door", Payload, CancellationToken.None);

        ProblemAssert.Is(result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task A_broker_that_does_not_answer_in_time_gets_the_sender_a_503_to_retry()
    {
        var engine = new Mock<IWorkflowEngineGateway>();
        engine.Setup(e => e.SendWakeAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var controller = CreateController(engine.Object);

        var result = await controller.Wake("token", Payload, CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal("5", controller.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task An_unreachable_broker_gets_the_sender_a_503()
    {
        var engine = new Mock<IWorkflowEngineGateway>();
        engine.Setup(e => e.SendWebhookAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unreachable"));

        var result = await CreateController(engine.Object).Webhook(Guid.NewGuid(), "door", Payload, CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public async Task A_sender_that_hangs_up_is_not_answered_with_a_503()
    {
        using var hungUp = new CancellationTokenSource();
        hungUp.Cancel();
        var engine = new Mock<IWorkflowEngineGateway>();
        engine.Setup(e => e.SendWakeAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        await Assert.ThrowsAsync<TaskCanceledException>(() => CreateController(engine.Object).Wake("token", Payload, hungUp.Token));
    }

    private static PublicCallbackController CreateController(IWorkflowEngineGateway engine) =>
        new(engine, NullLogger<PublicCallbackController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
}
