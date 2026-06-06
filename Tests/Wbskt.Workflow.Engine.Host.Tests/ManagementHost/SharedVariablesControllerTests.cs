using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.System;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;
using Wbskt.Primitives.Models;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class SharedVariablesControllerTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static (SharedVariablesController Controller, Mock<IWorkflowDefinitionService> WorkflowService, Mock<IAuthServiceClient> AuthClient) CreateController(
        ISharedVariableProvider provider,
        PermissionSlug permission)
    {
        var workflowService = new Mock<IWorkflowDefinitionService>();
        var authClient = new Mock<IAuthServiceClient>();
        authClient.Setup(x => x.ResolveWorkspaceAsync(WorkspaceRef, permission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceId);
        var controller = new SharedVariablesController(provider, workflowService.Object, authClient.Object);
        return (controller, workflowService, authClient);
    }

    [Fact]
    public async Task Get_resolves_workspace_validates_ownership_and_returns_dto()
    {
        var provider = new Mock<ISharedVariableProvider>();
        var workflowRefId = Guid.NewGuid();
        provider.Setup(x => x.GetByWorkflowRefIdNameAsync(workflowRefId, "counter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SharedVariableRow
            {
                Id = 1,
                WorkflowRefId = workflowRefId,
                VarName = "counter",
                VarType = "Counter",
                ValueJson = "1",
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        var (controller, workflowService, authClient) = CreateController(provider.Object, Permissions.WorkflowsRead);

        var dto = await controller.Get(WorkspaceRef, workflowRefId, "counter", CancellationToken.None);

        Assert.Equal(workflowRefId, dto.WorkflowRefId);
        Assert.Equal("counter", dto.VarName);
        Assert.Equal("1", dto.ValueJson);
        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsRead, It.IsAny<CancellationToken>()), Times.Once);
        workflowService.Verify(x => x.EnsureWorkflowInWorkspaceAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_resolves_workspace_validates_ownership_and_returns_dto()
    {
        var provider = new Mock<ISharedVariableProvider>();
        var workflowRefId = Guid.NewGuid();
        provider.Setup(x => x.SetAsync(workflowRefId, "counter", "2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SharedVariableRow
            {
                Id = 1,
                WorkflowRefId = workflowRefId,
                VarName = "counter",
                VarType = "Counter",
                ValueJson = "2",
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        var (controller, workflowService, authClient) = CreateController(provider.Object, Permissions.WorkflowsUpdate);

        var dto = await controller.Set(WorkspaceRef, workflowRefId, "counter", new SharedVariableSetRequest("2"), CancellationToken.None);

        Assert.Equal("2", dto.ValueJson);
        provider.Verify(x => x.SetAsync(workflowRefId, "counter", "2", It.IsAny<CancellationToken>()), Times.Once);
        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsUpdate, It.IsAny<CancellationToken>()), Times.Once);
        workflowService.Verify(x => x.EnsureWorkflowInWorkspaceAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_in_other_workspace_throws_security_and_skips_provider()
    {
        var provider = new Mock<ISharedVariableProvider>();
        var workflowRefId = Guid.NewGuid();
        var (controller, workflowService, _) = CreateController(provider.Object, Permissions.WorkflowsRead);
        workflowService.Setup(x => x.EnsureWorkflowInWorkspaceAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityException("denied"));

        await Assert.ThrowsAsync<SecurityException>(() => controller.Get(WorkspaceRef, workflowRefId, "counter", CancellationToken.None));
        provider.Verify(x => x.GetByWorkflowRefIdNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_not_found_throws_NotFoundException()
    {
        var provider = new Mock<ISharedVariableProvider>();
        var workflowRefId = Guid.NewGuid();
        provider.Setup(x => x.GetByWorkflowRefIdNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("missing"));
        var (controller, _, _) = CreateController(provider.Object, Permissions.WorkflowsRead);

        await Assert.ThrowsAsync<NotFoundException>(() => controller.Get(WorkspaceRef, workflowRefId, "missing", CancellationToken.None));
    }

    [Fact]
    public async Task GlobalExceptionMiddleware_maps_OptimisticConcurrencyException_to_409()
    {
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new OptimisticConcurrencyException("conflict"),
            NullLogger<GlobalExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var eventBus = new Mock<IEventBus>();

        await middleware.InvokeAsync(context, eventBus.Object);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        eventBus.Verify(x => x.PublishAsync(It.IsAny<SystemErrorEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
