using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.System;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class SharedVariablesControllerTests
{
    [Fact]
    public async Task Get_returns_shared_variable_dto()
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
        var controller = new SharedVariablesController(provider.Object);

        var dto = await controller.Get(workflowRefId, "counter", CancellationToken.None);

        Assert.Equal(workflowRefId, dto.WorkflowRefId);
        Assert.Equal("counter", dto.VarName);
        Assert.Equal("1", dto.ValueJson);
    }

    [Fact]
    public async Task Set_calls_provider_and_returns_dto()
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
        var controller = new SharedVariablesController(provider.Object);

        var dto = await controller.Set(workflowRefId, "counter", new SharedVariableSetRequest("2"), CancellationToken.None);

        Assert.Equal("2", dto.ValueJson);
        provider.Verify(x => x.SetAsync(workflowRefId, "counter", "2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_not_found_throws_NotFoundException()
    {
        var provider = new Mock<ISharedVariableProvider>();
        provider.Setup(x => x.GetByWorkflowRefIdNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("missing"));
        var controller = new SharedVariablesController(provider.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => controller.Get(Guid.NewGuid(), "missing", CancellationToken.None));
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
