using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Engine.Host.Handlers;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models.TriggerContexts;

namespace Wbskt.Workflow.Engine.Host.Tests.Handlers;

public class ClientPayloadTriggerHandlerTests
{
    private readonly Mock<IWorkflowRuntimeRegistry> _registryMock;
    private readonly Mock<IWorkflowEngine> _engineMock;
    private readonly Mock<ConsumeContext<ClientPayloadReceivedEvent>> _contextMock;
    private readonly ClientPayloadTriggerHandler _handler;

    public ClientPayloadTriggerHandlerTests()
    {
        _registryMock = new Mock<IWorkflowRuntimeRegistry>();
        _engineMock = new Mock<IWorkflowEngine>();
        var loggerMock = new Mock<ILogger<ClientPayloadTriggerHandler>>();
        _contextMock = new Mock<ConsumeContext<ClientPayloadReceivedEvent>>();
        
        _handler = new ClientPayloadTriggerHandler(
            _registryMock.Object, 
            _engineMock.Object, 
            loggerMock.Object);
    }

    [Fact]
    public async Task Consume_NotTelemetry_ReturnsEarly()
    {
        // Arrange
        _contextMock.Setup(x => x.Message).Returns(new ClientPayloadReceivedEvent(Guid.NewGuid(), 1, 1, "CommandResponse", ""));

        // Act
        await _handler.Consume(_contextMock.Object);

        // Assert
        _registryMock.Verify(x => x.GetWorkflows(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Consume_NoWorkflowsFound_ReturnsEarly()
    {
        // Arrange
        var clientRefId = Guid.NewGuid();
        _contextMock.Setup(x => x.Message).Returns(new ClientPayloadReceivedEvent(clientRefId, 5, 1, "Telemetry", ""));
        
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
        _contextMock.Setup(x => x.Message).Returns(new ClientPayloadReceivedEvent(clientRefId, 4, workspaceId, "Telemetry", "{\"temp\": 25}"));

        var workflow = new WorkflowDefinition { WorkflowRefId = Guid.NewGuid(), WorkspaceId = workspaceId };
        
        _registryMock.Setup(x => x.GetWorkflows($"client:{clientRefId}"))
            .Returns(new List<WorkflowDefinition> { workflow });

        // Act
        await _handler.Consume(_contextMock.Object);

        // Assert
        _engineMock.Verify(x => x.StartAsync(
            workflow, 
            It.Is<ClientPayloadTriggerContext>(c => c.DeviceRefId == clientRefId)), 
            Times.Once);
    }
}
