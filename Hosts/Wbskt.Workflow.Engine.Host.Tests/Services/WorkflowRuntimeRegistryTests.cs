using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Engine.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.Services;

public class WorkflowRuntimeRegistryTests
{
    private readonly WorkflowRuntimeRegistry _registry;

    public WorkflowRuntimeRegistryTests()
    {
        var loggerMock = new Mock<ILogger<WorkflowRuntimeRegistry>>();
        _registry = new WorkflowRuntimeRegistry(loggerMock.Object);
    }

    [Fact]
    public void RegisterWorkflow_WithDeviceTrigger_RegistersUnderClientKey()
    {
        // Arrange
        var clientRefId = Guid.NewGuid();
        var triggerNode = new DeviceTriggerNode { ClientRefId = clientRefId };
        var definition = new WorkflowDefinition
        {
            WorkflowRefId = Guid.NewGuid(),
            IsEnabled = true,
            Nodes = [triggerNode]
        };

        // Act
        _registry.RegisterWorkflow(definition);
        var workflows = _registry.GetWorkflows($"client:{clientRefId}");

        // Assert
        Assert.Single(workflows);
        Assert.Equal(definition.WorkflowRefId, workflows.First().WorkflowRefId);
    }

    [Fact]
    public void RegisterWorkflow_WithTimerTrigger_RegistersUnderTimerSystemKey()
    {
        // Arrange
        var triggerNode = new TimerScheduleNode { Interval = 10 };
        var definition = new WorkflowDefinition
        {
            WorkflowRefId = Guid.NewGuid(),
            IsEnabled = true,
            Nodes = [triggerNode]
        };

        // Act
        _registry.RegisterWorkflow(definition);
        var workflows = _registry.GetWorkflows("timer:system");

        // Assert
        Assert.Single(workflows);
        Assert.Equal(definition.WorkflowRefId, workflows.First().WorkflowRefId);
    }

    [Fact]
    public void GetWorkflows_DisabledWorkflow_ReturnsEmpty()
    {
        // Arrange
        var clientRefId = Guid.NewGuid();
        var triggerNode = new DeviceTriggerNode { ClientRefId = clientRefId };
        var definition = new WorkflowDefinition
        {
            WorkflowRefId = Guid.NewGuid(),
            IsEnabled = false, // Disabled
            Nodes = [triggerNode]
        };

        // Act
        _registry.RegisterWorkflow(definition);
        var workflows = _registry.GetWorkflows($"client:{clientRefId}");

        // Assert
        Assert.Empty(workflows);
    }

    [Fact]
    public void UnregisterWorkflow_RemovesFromTriggerMap()
    {
        // Arrange
        var clientRefId = Guid.NewGuid();
        var definition = new WorkflowDefinition
        {
            WorkflowRefId = Guid.NewGuid(),
            IsEnabled = true,
            Nodes = [new DeviceTriggerNode { ClientRefId = clientRefId }]
        };

        // Act
        _registry.RegisterWorkflow(definition);
        _registry.UnregisterWorkflow(definition.WorkflowRefId);
        var workflows = _registry.GetWorkflows($"client:{clientRefId}");

        // Assert
        Assert.Empty(workflows);
    }

    [Fact]
    public void Initialize_ClearsAndRegistersAll()
    {
        // Arrange
        var client1 = Guid.NewGuid();
        var client2 = Guid.NewGuid();
        
        var def1 = new WorkflowDefinition { WorkflowRefId = Guid.NewGuid(), IsEnabled = true, Nodes = [new DeviceTriggerNode { ClientRefId = client1 }] };
        var def2 = new WorkflowDefinition { WorkflowRefId = Guid.NewGuid(), IsEnabled = true, Nodes = [new DeviceTriggerNode { ClientRefId = client2 }] };

        var oldClient = Guid.NewGuid();
        _registry.RegisterWorkflow(new WorkflowDefinition { WorkflowRefId = Guid.NewGuid(), IsEnabled = true, Nodes = [new DeviceTriggerNode { ClientRefId = oldClient }] });

        // Act
        _registry.Initialize([def1, def2]);

        // Assert
        Assert.Single(_registry.GetWorkflows($"client:{client1}"));
        Assert.Single(_registry.GetWorkflows($"client:{client2}"));
        Assert.Empty(_registry.GetWorkflows($"client:{oldClient}"));
    }
}
