using System.Text.Json;
using Moq;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowsControllerTests
{
    [Fact]
    public async Task Publish_calls_service_and_returns_response()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var request = new WorkflowPublishRequest(Guid.NewGuid(), "Greenhouse", "desc", JsonSerializer.SerializeToElement(new { version = 1 }));
        var expected = new WorkflowPublishResponse(request.RefId, 1, "Published");
        service.Setup(x => x.PublishAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object);

        var actual = await controller.Publish(request, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.PublishAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrent_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 2, "Published", "Workflow", "desc", JsonSerializer.SerializeToElement(new { version = 2 }), DateTime.UtcNow);
        service.Setup(x => x.GetCurrentAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object);

        var actual = await controller.GetCurrent(refId, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.GetCurrentAsync(refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrent_calls_service_and_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 2, "Published", "Workflow", "desc", JsonSerializer.SerializeToElement(new { version = 2 }), DateTime.UtcNow);
        service.Setup(x => x.GetCurrentAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object);

        var actual = await controller.GetCurrent(refId, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.GetCurrentAsync(refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetVersion_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 3, "Published", "Workflow", null, JsonSerializer.SerializeToElement(new { version = 3 }), DateTime.UtcNow);
        service.Setup(x => x.GetVersionAsync(refId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object);

        var actual = await controller.GetVersion(refId, 3, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.GetVersionAsync(refId, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetVersion_calls_service_and_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 3, "Published", "Workflow", null, JsonSerializer.SerializeToElement(new { version = 3 }), DateTime.UtcNow);
        service.Setup(x => x.GetVersionAsync(refId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object);

        var actual = await controller.GetVersion(refId, 3, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.GetVersionAsync(refId, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deprecate_calls_service()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var refId = Guid.NewGuid();
        var controller = new WorkflowsController(service.Object, engineClient.Object);

        await controller.Deprecate(refId, CancellationToken.None);

        service.Verify(x => x.DeprecateAsync(refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartManualRun_calls_GetCurrentAsync_for_existence_check()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        service.Setup(x => x.GetCurrentAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinitionDto(workflowRefId, 1, "Published", "Workflow", null, JsonSerializer.SerializeToElement(new { version = 1 }), DateTime.UtcNow));
        engineClient.Setup(x => x.StartManualRunAsync(workflowRefId, It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartRunResponse(runRefId, 88));
        var controller = new WorkflowsController(service.Object, engineClient.Object);
        var request = new StartRunRequest("manual-node", new Dictionary<string, JsonElement>
        {
            ["body"] = JsonSerializer.SerializeToElement(new { value = 5 })
        });

        await controller.StartManualRun(workflowRefId, request, CancellationToken.None);

        service.Verify(x => x.GetCurrentAsync(workflowRefId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartManualRun_delegates_to_engine_client_and_returns_result()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        service.Setup(x => x.GetCurrentAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinitionDto(workflowRefId, 1, "Published", "Workflow", null, JsonSerializer.SerializeToElement(new { version = 1 }), DateTime.UtcNow));
        var expected = new StartRunResponse(runRefId, 88);
        engineClient.Setup(x => x.StartManualRunAsync(workflowRefId, It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object);
        var request = new StartRunRequest("manual-node", null);

        var response = await controller.StartManualRun(workflowRefId, request, CancellationToken.None);

        Assert.Equal(runRefId, response.RunRefId);
        Assert.Equal(88, response.RunId);
        engineClient.Verify(x => x.StartManualRunAsync(workflowRefId, request, It.IsAny<CancellationToken>()), Times.Once);
    }
}

