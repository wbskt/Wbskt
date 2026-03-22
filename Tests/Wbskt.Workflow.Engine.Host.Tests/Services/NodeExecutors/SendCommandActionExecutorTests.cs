using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Services.NodeExecutors;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services.NodeExecutors;

public class SendCommandActionExecutorTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly SendCommandActionExecutor _executor;

    public SendCommandActionExecutorTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _executor = new SendCommandActionExecutor(_eventBusMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_PublishesClientPayloadEvent()
    {
        // Arrange
        var targetClientRefId = Guid.NewGuid();
        const string payload = "{\"cmd\": \"start\"}";
        
        var node = new SendCommandActionNode
        {
            NodeId = Guid.NewGuid(),
            TargetClientRefId = targetClientRefId,
            MessageType = "ControlCommand",
            Payload = payload
        };
        
        var instance = new WorkflowInstance { WorkspaceId = 42 };
        var context = new ExecutionContext(instance, Guid.NewGuid());

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<ClientPayloadEvent>(), CancellationToken.None))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Out, result.ActivatedPortIds.First());
        
        // Ensure the event bus was called with exactly the expected properties
        _eventBusMock.Verify(x => x.PublishAsync(It.Is<ClientPayloadEvent>(e => 
            e.ClientRefId == targetClientRefId &&
            e.WorkspaceId == 42 &&
            e.MessageType == "ControlCommand"
        ), CancellationToken.None), Times.Once);
    }
}
