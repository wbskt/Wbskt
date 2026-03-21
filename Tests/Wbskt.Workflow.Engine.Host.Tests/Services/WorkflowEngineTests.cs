using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.WorkflowEngine;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Engine.Host.Enums;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Models.TriggerContexts;
using Wbskt.Workflow.Engine.Host.Services;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services;

public class WorkflowEngineTests
{
    private readonly Mock<IKeyedServiceProvider> _serviceProviderMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly WorkflowEngine _engine;

    public WorkflowEngineTests()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        _serviceProviderMock = new Mock<IKeyedServiceProvider>();
        
        scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);
        scopeMock.Setup(x => x.ServiceProvider).Returns(_serviceProviderMock.Object);
        
        _eventBusMock = new Mock<IEventBus>();
        var loggerMock = new Mock<ILogger<WorkflowEngine>>();

        _engine = new WorkflowEngine(
            scopeFactoryMock.Object,
            _eventBusMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task StartAsync_NoMatchingTriggers_CompletesInstantly()
    {
        // Arrange
        var definition = new WorkflowDefinition
        {
            WorkflowRefId = Guid.NewGuid(),
            WorkspaceId = 1,
            Nodes = [ new DeviceTriggerNode { TriggerType = DeviceTriggerType.OnTelemetry, ClientRefId = Guid.NewGuid() } ],
            Edges = []
        };
        
        var triggerContext = new ClientPayloadTriggerContext { DeviceRefId = Guid.NewGuid() };

        // Act
        var instance = await _engine.StartAsync(definition, triggerContext);

        // Assert
        Assert.Equal(WorkflowStatus.Completed, instance.Status);
        
        _eventBusMock.Verify(x => x.PublishAsync(It.IsAny<WorkflowInstanceStartedEvent>(), CancellationToken.None), Times.Once);
        _eventBusMock.Verify(x => x.PublishAsync(It.IsAny<WorkflowInstanceCompletedEvent>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task StartAsync_MatchingTrigger_ExecutesNodesSuccessfully()
    {
        // Arrange
        var clientRefId = Guid.NewGuid();
        var triggerNodeId = Guid.NewGuid();
        var actionNodeId = Guid.NewGuid();

        var triggerNode = new DeviceTriggerNode
        {
            NodeId = triggerNodeId,
            TriggerType = DeviceTriggerType.OnTelemetry,
            ClientRefId = clientRefId
        };

        var actionNode = new SendCommandActionNode
        {
            NodeId = actionNodeId
        };

        var definition = new WorkflowDefinition
        {
            WorkflowRefId = Guid.NewGuid(),
            WorkspaceId = 1,
            Nodes = [triggerNode, actionNode],
            Edges = [
                new WorkflowEdge { 
                    Source = new WorkflowPort { NodeId = triggerNodeId, PortId = PortNames.Out },
                    Target = new WorkflowPort { NodeId = actionNodeId, PortId = PortNames.In }
                }
            ]
        };

        var triggerContext = new ClientPayloadTriggerContext { DeviceRefId = clientRefId };

        var triggerExecutorMock = new Mock<IWorkflowNodeExecutor>();
        triggerExecutorMock.Setup(x => x.ExecuteAsync(triggerNode, It.IsAny<ExecutionContext>()))
            .ReturnsAsync(NodeExecutionResult.Success(PortNames.Out));

        var actionExecutorMock = new Mock<IWorkflowNodeExecutor>();
        actionExecutorMock.Setup(x => x.ExecuteAsync(actionNode, It.IsAny<ExecutionContext>()))
            .ReturnsAsync(NodeExecutionResult.Success(PortNames.Out));

        // Setup KeyedServiceProvider
        _serviceProviderMock.Setup(x => x.GetKeyedService(typeof(IWorkflowNodeExecutor), nameof(DeviceTriggerNode)))
            .Returns(triggerExecutorMock.Object);
            
        _serviceProviderMock.Setup(x => x.GetKeyedService(typeof(IWorkflowNodeExecutor), nameof(SendCommandActionNode)))
            .Returns(actionExecutorMock.Object);

        // Act
        var instance = await _engine.StartAsync(definition, triggerContext);
        
        // Let the engine exhaust its fire-and-forget tasks inline
        // If mocks are synchronous, they run on the same logical thread path without yielding.
        // Wait briefly just in case it yields somewhere.
        await Task.Delay(50);

        // Assert
        Assert.Equal(WorkflowStatus.Completed, instance.Status);
        Assert.Single(instance.Pointers);
        Assert.Equal(ExecutionStatus.Completed, instance.Pointers[0].Status);
        
        triggerExecutorMock.Verify(x => x.ExecuteAsync(triggerNode, It.IsAny<ExecutionContext>()), Times.Once);
        actionExecutorMock.Verify(x => x.ExecuteAsync(actionNode, It.IsAny<ExecutionContext>()), Times.Once);
        
        _eventBusMock.Verify(x => x.PublishAsync(It.IsAny<WorkflowInstanceCompletedEvent>(), CancellationToken.None), Times.Once);
    }
}
