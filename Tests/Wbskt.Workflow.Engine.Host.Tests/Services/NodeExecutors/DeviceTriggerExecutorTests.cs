using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Models.TriggerContexts;
using Wbskt.Workflow.Engine.Host.Services.NodeExecutors;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services.NodeExecutors;

public class DeviceTriggerExecutorTests
{
    private readonly DeviceTriggerExecutor _executor = new();

    [Fact]
    public async Task ExecuteAsync_ClientPayloadTrigger_SetsDataToLastOutput()
    {
        // Arrange
        var node = new DeviceTriggerNode
        {
            NodeId = Guid.NewGuid(),
        };

        var triggerContext = new ClientPayloadTriggerContext
        {
            DeviceRefId = Guid.NewGuid(),
            Data = JsonDocument.Parse("{\"status\": \"active\"}").RootElement
        };

        var instance = new WorkflowInstance { TriggerContext = triggerContext };
        var context = new ExecutionContext(instance, Guid.NewGuid());

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Out, result.ActivatedPortIds[0]);
        Assert.NotNull(context.LastNodeOutput);
    }
    
    [Fact]
    public async Task ExecuteAsync_ClientPropertyChangeTrigger_SetsNewValueToLastOutput()
    {
        // Arrange
        var node = new DeviceTriggerNode
        {
            NodeId = Guid.NewGuid(),
        };

        var triggerContext = new ClientPropertyChangeTriggerContext
        {
            DeviceRefId = Guid.NewGuid(),
            PropertyName = "temperature",
            NewValue = 25.5
        };

        var instance = new WorkflowInstance { TriggerContext = triggerContext };
        var context = new ExecutionContext(instance, Guid.NewGuid());

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Out, result.ActivatedPortIds[0]);
        Assert.Equal(25.5, context.LastNodeOutput);
    }
}
