using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models.Workflow;

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
}
