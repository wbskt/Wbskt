using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Management.Host.Controllers.Workflow;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class PublicCallbackControllerTests
{
    private static readonly JsonElement Payload = JsonDocument.Parse("{}").RootElement;

    [Fact]
    public async Task An_engine_that_times_out_gets_the_sender_a_503_to_retry()
    {
        var engine = new Mock<IWorkflowEngineClient>();
        engine.Setup(e => e.WakeAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()));
        var controller = CreateController(engine.Object);

        var result = await controller.Wake("token", Payload, CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal("5", controller.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task An_unreachable_engine_gets_the_sender_a_503()
    {
        var engine = new Mock<IWorkflowEngineClient>();
        engine.Setup(e => e.WebhookAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await CreateController(engine.Object).Webhook(Guid.NewGuid(), "door", Payload, CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public async Task A_sender_that_hangs_up_is_not_answered_with_a_503()
    {
        using var hungUp = new CancellationTokenSource();
        hungUp.Cancel();
        var engine = new Mock<IWorkflowEngineClient>();
        engine.Setup(e => e.WakeAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        await Assert.ThrowsAsync<TaskCanceledException>(() => CreateController(engine.Object).Wake("token", Payload, hungUp.Token));
    }

    private static PublicCallbackController CreateController(IWorkflowEngineClient engine) =>
        new(engine, NullLogger<PublicCallbackController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
}
