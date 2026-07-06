using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.System;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Management.Host.Controllers.Workflow;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
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
            .ReturnsAsync(Result<int>.Success(WorkspaceId));
        var controller = new SharedVariablesController(provider, workflowService.Object, authClient.Object, Mock.Of<ILogger<SharedVariablesController>>());
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
        workflowService.Setup(x => x.EnsureWorkflowInWorkspaceAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var response = await controller.Get(WorkspaceRef, workflowRefId, "counter", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var dto = Assert.IsType<SharedVariableDto>(okResult.Value);
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
        workflowService.Setup(x => x.EnsureWorkflowInWorkspaceAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var response = await controller.Set(WorkspaceRef, workflowRefId, "counter", new SharedVariableSetRequest("2"), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var dto = Assert.IsType<SharedVariableDto>(okResult.Value);
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
            .ReturnsAsync(Result.Failure(Error.Unauthorized("WORKFLOW_UNAUTHORIZED", "denied")));

        var result = await controller.Get(WorkspaceRef, workflowRefId, "counter", CancellationToken.None);
        
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        provider.Verify(x => x.GetByWorkflowRefIdNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_not_found_throws_NotFoundException()
    {
        var provider = new Mock<ISharedVariableProvider>();
        var workflowRefId = Guid.NewGuid();
        provider.Setup(x => x.GetByWorkflowRefIdNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("missing"));
        var (controller, workflowService, _) = CreateController(provider.Object, Permissions.WorkflowsRead);
        workflowService.Setup(x => x.EnsureWorkflowInWorkspaceAsync(WorkspaceId, workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await controller.Get(WorkspaceRef, workflowRefId, "missing", CancellationToken.None);
        
        Assert.IsType<NotFoundObjectResult>(result.Result);
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
