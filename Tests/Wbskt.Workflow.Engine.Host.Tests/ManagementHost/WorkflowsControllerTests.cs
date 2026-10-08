using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Controllers.Workflow;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Models;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowsControllerTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task Publish_resolves_workspace_with_create_permission_and_delegates()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var request = new WorkflowPublishRequest(Guid.NewGuid(), "Greenhouse", "desc", null!);
        var expected = new WorkflowPublishResponse(request.RefId, 1, "Published");
        service.Setup(x => x.PublishAsync(WorkspaceId, WorkspaceRef, request, It.IsAny<CancellationToken>())).ReturnsAsync(Result<WorkflowPublishResponse>.Success(expected));
        var controller = new WorkflowsController(service.Object, engineClient.Object, Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowsController>>());

        var actual = await controller.Publish(WorkspaceRef, WorkspaceId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actual.Result);
        Assert.Equal(expected, okResult.Value);
        service.Verify(x => x.PublishAsync(WorkspaceId, WorkspaceRef, request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrent_resolves_workspace_with_read_permission_and_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 2, "Published", "Workflow", "desc", null!, DateTime.UtcNow);
        service.Setup(x => x.GetCurrentAsync(WorkspaceId, refId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<WorkflowDefinitionDto>.Success(expected));
        var controller = new WorkflowsController(service.Object, engineClient.Object, Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowsController>>());

        var actual = await controller.GetCurrent(WorkspaceId, refId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actual.Result);
        Assert.Equal(expected, okResult.Value);
        service.Verify(x => x.GetCurrentAsync(WorkspaceId, refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetVersion_resolves_workspace_with_read_permission_and_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 3, "Published", "Workflow", null, null!, DateTime.UtcNow);
        service.Setup(x => x.GetVersionAsync(WorkspaceId, refId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Result<WorkflowDefinitionDto>.Success(expected));
        var controller = new WorkflowsController(service.Object, engineClient.Object, Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowsController>>());

        var actual = await controller.GetVersion(WorkspaceId, refId, 3, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actual.Result);
        Assert.Equal(expected, okResult.Value);
        service.Verify(x => x.GetVersionAsync(WorkspaceId, refId, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deprecate_resolves_workspace_with_delete_permission_and_delegates()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var refId = Guid.NewGuid();
        service.Setup(x => x.DeprecateAsync(WorkspaceId, refId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var controller = new WorkflowsController(service.Object, engineClient.Object, Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowsController>>());

        var result = await controller.Deprecate(WorkspaceId, refId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        service.Verify(x => x.DeprecateAsync(WorkspaceId, refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartManualRun_validates_ownership_then_delegates_to_engine_client()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        service.Setup(x => x.GetCurrentAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowDefinitionDto>.Success(new WorkflowDefinitionDto(workflowRefId, 1, "Published", "Workflow", null, null!, DateTime.UtcNow)));
        var expected = new StartRunResponse(runRefId, 88);
        engineClient.Setup(x => x.StartManualRunAsync(workflowRefId, It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var bus = new Mock<IEventBus>();
        var controller = new WorkflowsController(service.Object, engineClient.Object, bus.Object, Mock.Of<ILogger<WorkflowsController>>());
        var request = new StartRunRequest("manual-node", null);

        var response = await controller.StartManualRun(WorkspaceId, workflowRefId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var startRunResponse = Assert.IsType<StartRunResponse>(okResult.Value);
        Assert.Equal(runRefId, startRunResponse.RunRefId);
        Assert.Equal(88, startRunResponse.RunId);
        service.Verify(x => x.GetCurrentAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()), Times.Once);
        engineClient.Verify(x => x.StartManualRunAsync(workflowRefId, request, It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowRunRequestedEvent>(e => e.WorkflowRefId == workflowRefId && e.WorkspaceId == WorkspaceId && e.RunRefId == runRefId && e.Outcome == "Started"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartManualRun_that_the_engine_drops_records_nothing()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var workflowRefId = Guid.NewGuid();
        service.Setup(x => x.GetCurrentAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowDefinitionDto>.Success(new WorkflowDefinitionDto(workflowRefId, 1, "Published", "Workflow", null, null!, DateTime.UtcNow)));
        engineClient.Setup(x => x.StartManualRunAsync(workflowRefId, It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartRunResponse(Guid.Empty, 0, StartRunOutcome.Dropped));
        var bus = new Mock<IEventBus>();
        var controller = new WorkflowsController(service.Object, engineClient.Object, bus.Object, Mock.Of<ILogger<WorkflowsController>>());

        await controller.StartManualRun(WorkspaceId, workflowRefId, new StartRunRequest("manual-node", null), CancellationToken.None);

        bus.VerifyNoOtherCalls();
    }
}
