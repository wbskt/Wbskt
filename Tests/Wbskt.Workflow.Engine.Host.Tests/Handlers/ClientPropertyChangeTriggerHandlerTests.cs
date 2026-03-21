using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Engine.Host.Handlers;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models.TriggerContexts;

namespace Wbskt.Workflow.Engine.Host.Tests.Handlers;

public class ClientPropertyChangeTriggerHandlerTests
{
    private readonly Mock<IWorkflowRuntimeRegistry> _registryMock;
    private readonly Mock<IWorkflowEngine> _engineMock;
    private readonly Mock<ConsumeContext<ClientPropertyUpdatedEvent>> _contextMock;
    private readonly ClientPropertyChangeTriggerHandler _handler;

    public ClientPropertyChangeTriggerHandlerTests()
    {
        _registryMock = new Mock<IWorkflowRuntimeRegistry>();
        _engineMock = new Mock<IWorkflowEngine>();
        var loggerMock = new Mock<ILogger<ClientPropertyChangeTriggerHandler>>();
        _contextMock = new Mock<ConsumeContext<ClientPropertyUpdatedEvent>>();
        
        _handler = new ClientPropertyChangeTriggerHandler(
            _registryMock.Object, 
            _engineMock.Object, 
            loggerMock.Object);
    }

    [Fact]
    public async Task Consume_NoWorkflowsFound_ReturnsEarly()
    {
        // Arrange
        var clientRefId = Guid.NewGuid();
        _contextMock.Setup(x => x.Message).Returns(new ClientPropertyUpdatedEvent(clientRefId, 5, 1, "firmware_version", "2.0.0"));
        
        _registryMock.Setup(x => x.GetWorkflows($"client:{clientRefId}"))
            .Returns(new List<WorkflowDefinition>());

        // Act
        await _handler.Consume(_contextMock.Object);

        // Assert
        _engineMock.Verify(x => x.StartAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<BaseTriggerContext>()), Times.Never);
    }

    [Fact]
    public async Task Consume_MatchingWorkflows_StartsEngine()
    {
        // Arrange
        var clientRefId = Guid.NewGuid();
        var workspaceId = 1;
        _contextMock.Setup(x => x.Message).Returns(new ClientPropertyUpdatedEvent(clientRefId, 6, workspaceId, "firmware_version", "2.0.0"));

        var workflow = new WorkflowDefinition { WorkflowRefId = Guid.NewGuid(), WorkspaceId = workspaceId };
        
        _registryMock.Setup(x => x.GetWorkflows($"client:{clientRefId}"))
            .Returns(new List<WorkflowDefinition> { workflow });

        // Act
        await _handler.Consume(_contextMock.Object);

        // Assert
        _engineMock.Verify(x => x.StartAsync(
            workflow, 
            It.Is<ClientPropertyChangeTriggerContext>(c => 
                c.DeviceRefId == clientRefId && 
                c.PropertyName == "firmware_version")), 
            Times.Once);
    }
}
