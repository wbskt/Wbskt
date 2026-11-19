using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
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
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class WorkflowsControllerTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task Publish_resolves_workspace_with_create_permission_and_delegates()
    {
        var service = new Mock<IWorkflowLifecycleService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var request = new WorkflowPublishRequest(Guid.NewGuid(), "Greenhouse", "desc", null!, Enable: true);
        var expected = new WorkflowPublishResponse(request.RefId, 1, WorkflowState.Enabled, 1);
        service.Setup(x => x.PublishAsync(WorkspaceId, WorkspaceRef, request, It.IsAny<CancellationToken>())).ReturnsAsync(Result<WorkflowPublishResponse>.Success(expected));
        var controller = new WorkflowsController(Mock.Of<IWorkflowQueryService>(), service.Object, new WorkflowRunService(Mock.Of<IWorkflowStateProvider>(), Mock.Of<IWorkflowRunQueryService>(), engineClient.Object, Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowRunService>>()));

        var actual = await controller.Publish(WorkspaceRef, WorkspaceId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actual.Result);
        Assert.Equal(expected, okResult.Value);
        service.Verify(x => x.PublishAsync(WorkspaceId, WorkspaceRef, request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrent_resolves_workspace_with_read_permission_and_returns_dto()
    {
        var service = new Mock<IWorkflowQueryService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 2, WorkflowState.Enabled, 2, "Workflow", "desc", null!, DateTime.UtcNow);
        service.Setup(x => x.GetCurrentAsync(WorkspaceId, refId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<WorkflowDefinitionDto>.Success(expected));
        var controller = new WorkflowsController(service.Object, Mock.Of<IWorkflowLifecycleService>(), new WorkflowRunService(Mock.Of<IWorkflowStateProvider>(), Mock.Of<IWorkflowRunQueryService>(), engineClient.Object, Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowRunService>>()));

        var actual = await controller.GetCurrent(WorkspaceId, refId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actual.Result);
        Assert.Equal(expected, okResult.Value);
        service.Verify(x => x.GetCurrentAsync(WorkspaceId, refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetVersion_resolves_workspace_with_read_permission_and_returns_dto()
    {
        var service = new Mock<IWorkflowQueryService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 3, WorkflowState.Enabled, 3, "Workflow", null, null!, DateTime.UtcNow);
        service.Setup(x => x.GetVersionAsync(WorkspaceId, refId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Result<WorkflowDefinitionDto>.Success(expected));
        var controller = new WorkflowsController(service.Object, Mock.Of<IWorkflowLifecycleService>(), new WorkflowRunService(Mock.Of<IWorkflowStateProvider>(), Mock.Of<IWorkflowRunQueryService>(), engineClient.Object, Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowRunService>>()));

        var actual = await controller.GetVersion(WorkspaceId, refId, 3, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actual.Result);
        Assert.Equal(expected, okResult.Value);
        service.Verify(x => x.GetVersionAsync(WorkspaceId, refId, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enable_delegates_the_version_and_returns_the_new_state()
    {
        var service = new Mock<IWorkflowLifecycleService>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowStateResponse(refId, WorkflowState.Enabled, 3);
        service.Setup(x => x.EnableAsync(WorkspaceId, WorkspaceRef, refId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Result<WorkflowStateResponse>.Success(expected));

        var actual = await Controller(service).Enable(WorkspaceRef, WorkspaceId, refId, new WorkflowEnableRequest(3), CancellationToken.None);

        Assert.Equal(expected, Assert.IsType<OkObjectResult>(actual.Result).Value);
    }

    [Fact]
    public async Task Disable_passes_whether_to_cancel_running_runs()
    {
        var service = new Mock<IWorkflowLifecycleService>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowStateResponse(refId, WorkflowState.Disabled, null, CancelledRuns: 2);
        service.Setup(x => x.DisableAsync(WorkspaceId, refId, true, It.IsAny<CancellationToken>())).ReturnsAsync(Result<WorkflowStateResponse>.Success(expected));

        var actual = await Controller(service).Disable(WorkspaceId, refId, new WorkflowDisableRequest(CancelRunningRuns: true), CancellationToken.None);

        Assert.Equal(expected, Assert.IsType<OkObjectResult>(actual.Result).Value);
    }

    [Fact]
    public async Task Enable_that_the_service_refuses_answers_its_error()
    {
        var service = new Mock<IWorkflowLifecycleService>();
        var refId = Guid.NewGuid();
        service.Setup(x => x.EnableAsync(WorkspaceId, WorkspaceRef, refId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowStateResponse>.Failure(Error.Conflict("WORKFLOW_DEPRECATED", "deprecated")));

        var actual = await Controller(service).Enable(WorkspaceRef, WorkspaceId, refId, new WorkflowEnableRequest(1), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeActionResult>(actual.Result).StatusCode);
    }

    [Fact]
    public async Task Deprecate_delegates_and_returns_the_new_state()
    {
        var service = new Mock<IWorkflowLifecycleService>();
        var refId = Guid.NewGuid();
        var expected = new WorkflowStateResponse(refId, WorkflowState.Deprecated, null);
        service.Setup(x => x.DeprecateAsync(WorkspaceId, refId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<WorkflowStateResponse>.Success(expected));

        var actual = await Controller(service).Deprecate(WorkspaceId, refId, CancellationToken.None);

        Assert.Equal(expected, Assert.IsType<OkObjectResult>(actual.Result).Value);
    }

    [Fact]
    public async Task StartManualRun_validates_ownership_then_delegates_to_engine_client()
    {
        var service = new Mock<IWorkflowQueryService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var states = new Mock<IWorkflowStateProvider>();
        states.Setup(x => x.FindAsync(workflowRefId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowStateRow(workflowRefId, WorkspaceId, WorkflowState.Enabled, 1, 1, 1));
        var expected = new StartRunResponse(runRefId);
        engineClient.Setup(x => x.StartManualRunAsync(workflowRefId, It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var bus = new Mock<IEventBus>();
        var controller = new WorkflowsController(service.Object, Mock.Of<IWorkflowLifecycleService>(), new WorkflowRunService(states.Object, Mock.Of<IWorkflowRunQueryService>(), engineClient.Object, bus.Object, Mock.Of<ILogger<WorkflowRunService>>()));
        var request = new StartRunRequest(Guid.NewGuid(), null);

        var response = await controller.StartManualRun(WorkspaceId, workflowRefId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var startRunResponse = Assert.IsType<StartRunResponse>(okResult.Value);
        Assert.Equal(runRefId, startRunResponse.RunRefId);
        states.Verify(x => x.FindAsync(workflowRefId, WorkspaceId, It.IsAny<CancellationToken>()), Times.Once);
        engineClient.Verify(x => x.StartManualRunAsync(workflowRefId, request, It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowRunRequestedEvent>(e => e.WorkflowRefId == workflowRefId && e.WorkspaceId == WorkspaceId && e.RunRefId == runRefId && e.Outcome == "Started"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartManualRun_that_the_engine_drops_records_nothing()
    {
        var service = new Mock<IWorkflowQueryService>();
        var engineClient = new Mock<IWorkflowEngineGateway>();
        var workflowRefId = Guid.NewGuid();
        var states = new Mock<IWorkflowStateProvider>();
        states.Setup(x => x.FindAsync(workflowRefId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowStateRow(workflowRefId, WorkspaceId, WorkflowState.Enabled, 1, 1, 1));
        engineClient.Setup(x => x.StartManualRunAsync(workflowRefId, It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartRunResponse(Guid.Empty, StartRunOutcome.Dropped));
        var bus = new Mock<IEventBus>();
        var controller = new WorkflowsController(service.Object, Mock.Of<IWorkflowLifecycleService>(), new WorkflowRunService(states.Object, Mock.Of<IWorkflowRunQueryService>(), engineClient.Object, bus.Object, Mock.Of<ILogger<WorkflowRunService>>()));

        await controller.StartManualRun(WorkspaceId, workflowRefId, new StartRunRequest(Guid.NewGuid(), null), CancellationToken.None);

        bus.VerifyNoOtherCalls();
    }

    private static WorkflowsController Controller(Mock<IWorkflowLifecycleService> lifecycle) =>
        new(Mock.Of<IWorkflowQueryService>(), lifecycle.Object, new WorkflowRunService(Mock.Of<IWorkflowStateProvider>(), Mock.Of<IWorkflowRunQueryService>(), Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowRunService>>()));
}
