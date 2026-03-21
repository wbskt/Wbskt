using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Services.NodeExecutors;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services.NodeExecutors;

public class DelayExecutorTests
{
    private readonly DelayExecutor _executor = new();

    [Fact]
    public async Task ExecuteAsync_ZeroSeconds_ReturnsOutPort()
    {
        // Arrange
        var node = new DelayNode
        {
            NodeId = Guid.NewGuid(),
            Seconds = 0
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.WaitUntil.HasValue);
        Assert.Equal(PortNames.Out, result.ActivatedPortIds[0]);
    }

    [Fact]
    public async Task ExecuteAsync_PositiveSeconds_ReturnsWaitResult()
    {
        // Arrange
        var node = new DelayNode
        {
            NodeId = Guid.NewGuid(),
            Seconds = 10
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());

        var expectedMinTime = DateTime.UtcNow.AddSeconds(9);
        var expectedMaxTime = DateTime.UtcNow.AddSeconds(11);

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.WaitUntil.HasValue);
        Assert.NotNull(result.WaitUntil);
        Assert.True(result.WaitUntil >= expectedMinTime && result.WaitUntil <= expectedMaxTime, 
            $"Expected WaitUntil to be between {expectedMinTime} and {expectedMaxTime}, but was {result.WaitUntil}");
    }
}
