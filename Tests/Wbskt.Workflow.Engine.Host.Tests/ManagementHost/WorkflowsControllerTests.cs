using System.Text.Json;
using Moq;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Models;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowsControllerTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static Mock<IAuthServiceClient> AuthClientFor(PermissionSlug permission)
    {
        var authClient = new Mock<IAuthServiceClient>();
        authClient.Setup(x => x.ResolveWorkspaceAsync(WorkspaceRef, permission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceId);
        return authClient;
    }

    [Fact]
    public async Task Publish_resolves_workspace_with_create_permission_and_delegates()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsCreate);
        var request = new WorkflowPublishRequest(Guid.NewGuid(), "Greenhouse", "desc", JsonSerializer.SerializeToElement(new { version = 1 }));
        var expected = new WorkflowPublishResponse(request.RefId, 1, "Published");
        service.Setup(x => x.PublishAsync(WorkspaceId, request, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object, authClient.Object);

        var actual = await controller.Publish(WorkspaceRef, request, CancellationToken.None);

        Assert.Equal(expected, actual);
        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsCreate, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.PublishAsync(WorkspaceId, request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrent_resolves_workspace_with_read_permission_and_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsRead);
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 2, "Published", "Workflow", "desc", JsonSerializer.SerializeToElement(new { version = 2 }), DateTime.UtcNow);
        service.Setup(x => x.GetCurrentAsync(WorkspaceId, refId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object, authClient.Object);

        var actual = await controller.GetCurrent(WorkspaceRef, refId, CancellationToken.None);

        Assert.Equal(expected, actual);
        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsRead, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.GetCurrentAsync(WorkspaceId, refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetVersion_resolves_workspace_with_read_permission_and_returns_dto()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsRead);
        var refId = Guid.NewGuid();
        var expected = new WorkflowDefinitionDto(refId, 3, "Published", "Workflow", null, JsonSerializer.SerializeToElement(new { version = 3 }), DateTime.UtcNow);
        service.Setup(x => x.GetVersionAsync(WorkspaceId, refId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object, authClient.Object);

        var actual = await controller.GetVersion(WorkspaceRef, refId, 3, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.GetVersionAsync(WorkspaceId, refId, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deprecate_resolves_workspace_with_delete_permission_and_delegates()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsDelete);
        var refId = Guid.NewGuid();
        var controller = new WorkflowsController(service.Object, engineClient.Object, authClient.Object);

        await controller.Deprecate(WorkspaceRef, refId, CancellationToken.None);

        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsDelete, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.DeprecateAsync(WorkspaceId, refId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartManualRun_validates_ownership_then_delegates_to_engine_client()
    {
        var service = new Mock<IWorkflowDefinitionService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsUpdate);
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        service.Setup(x => x.GetCurrentAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinitionDto(workflowRefId, 1, "Published", "Workflow", null, JsonSerializer.SerializeToElement(new { version = 1 }), DateTime.UtcNow));
        var expected = new StartRunResponse(runRefId, 88);
        engineClient.Setup(x => x.StartManualRunAsync(workflowRefId, It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new WorkflowsController(service.Object, engineClient.Object, authClient.Object);
        var request = new StartRunRequest("manual-node", null);

        var response = await controller.StartManualRun(WorkspaceRef, workflowRefId, request, CancellationToken.None);

        Assert.Equal(runRefId, response.RunRefId);
        Assert.Equal(88, response.RunId);
        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsUpdate, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.GetCurrentAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()), Times.Once);
        engineClient.Verify(x => x.StartManualRunAsync(workflowRefId, request, It.IsAny<CancellationToken>()), Times.Once);
    }
}
