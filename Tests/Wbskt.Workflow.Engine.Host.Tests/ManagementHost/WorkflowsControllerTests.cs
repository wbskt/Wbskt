using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowsControllerTests
{
    [Fact]
    public async Task Publish_calls_service_and_returns_response()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var request = new WorkflowPublishRequest(Guid.NewGuid(), "Greenhouse", "desc", JsonSerializer.SerializeToElement(new { version = 1 }));
        var expected = new WorkflowPublishResponse(request.RefId, 1, "Published");
        service.Setup(x => x.PublishAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object);

        var actual = await controller.Publish(request, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.PublishAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrent_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 2, "Published", "Workflow", "desc", JsonSerializer.SerializeToElement(new { version = 2 }), DateTime.UtcNow);
        service.Setup(x => x.GetCurrentAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object);

        var actual = await controller.GetCurrent(refId, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.GetCurrentAsync(refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrent_calls_service_and_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 2, "Published", "Workflow", "desc", JsonSerializer.SerializeToElement(new { version = 2 }), DateTime.UtcNow);
        service.Setup(x => x.GetCurrentAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object);

        var actual = await controller.GetCurrent(refId, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.GetCurrentAsync(refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetVersion_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 3, "Published", "Workflow", null, JsonSerializer.SerializeToElement(new { version = 3 }), DateTime.UtcNow);
        service.Setup(x => x.GetVersionAsync(refId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object);

        var actual = await controller.GetVersion(refId, 3, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.GetVersionAsync(refId, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetVersion_calls_service_and_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 3, "Published", "Workflow", null, JsonSerializer.SerializeToElement(new { version = 3 }), DateTime.UtcNow);
        service.Setup(x => x.GetVersionAsync(refId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object);

        var actual = await controller.GetVersion(refId, 3, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.GetVersionAsync(refId, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deprecate_calls_service()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var refId = Guid.NewGuid();
        var controller = new WorkflowsController(service.Object);

        await controller.Deprecate(refId, CancellationToken.None);

        service.Verify(x => x.DeprecateAsync(refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartManualRun_calls_inbound_hub()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var inboundHub = new Mock<IInboundHub>();
        var runProvider = new Mock<IRunProvider>();
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        service.Setup(x => x.GetCurrentAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinitionDto(workflowRefId, 1, "Published", "Workflow", null, JsonSerializer.SerializeToElement(new { version = 1 }), DateTime.UtcNow));
        inboundHub.Setup(x => x.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 88, null, "started"));
        runProvider.Setup(x => x.GetByIdAsync(88, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunRow
            {
                Id = 88,
                RefId = runRefId,
                WorkflowDefinitionId = 9,
                WorkflowRefId = workflowRefId,
                WorkflowVersion = 1,
                TriggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CorrelationKey = "manual",
                Status = "Running",
                StartedAt = DateTime.UtcNow,
                CompletedAt = null,
                CancellationRequestedAt = null,
                CancellationReason = null,
                CreditBudget = 10m,
                CreatedAt = DateTime.UtcNow
            });
        var controller = new WorkflowsController(service.Object, inboundHub.Object, runProvider.Object);

        var response = await controller.StartManualRun(workflowRefId, new StartRunRequest("manual-node", new Dictionary<string, JsonElement>
        {
            ["body"] = JsonSerializer.SerializeToElement(new { value = 5 })
        }), CancellationToken.None);

        Assert.Equal(runRefId, response.RunRefId);
        Assert.Equal(88, response.RunId);
        inboundHub.Verify(x => x.HandleAsync(It.Is<InboundEvent>(evt => evt.ChannelKind == "manual" && evt.CorrelationKey == "manual-node"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
