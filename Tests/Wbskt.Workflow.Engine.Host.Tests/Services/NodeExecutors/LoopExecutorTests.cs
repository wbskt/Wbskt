using Moq;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Services.NodeExecutors;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services.NodeExecutors;

public class LoopExecutorTests
{
    private readonly Mock<IWorkflowExpressionEvaluator> _evaluatorMock;
    private readonly LoopExecutor _executor;

    public LoopExecutorTests()
    {
        _evaluatorMock = new Mock<IWorkflowExpressionEvaluator>();
        _executor = new LoopExecutor(_evaluatorMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ItemsNotEnumerable_ReturnsCompletedPort()
    {
        // Arrange
        var expr = new LiteralExpression { Value = null };
        var node = new LoopNode
        {
            NodeId = Guid.NewGuid(),
            ItemsExpression = expr,
            IteratorName = "item"
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());
        
        // Return a single object (int), not an IEnumerable
        _evaluatorMock.Setup(x => x.Evaluate(expr, context)).Returns(123);

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Completed, result.ActivatedPortIds[0]);
    }

    [Fact]
    public async Task ExecuteAsync_IteratesThroughItems_AndCompletes()
    {
        // Arrange
        var expr = new LiteralExpression { Value = null };
        var nodeId = Guid.NewGuid();
        var node = new LoopNode
        {
            NodeId = nodeId,
            ItemsExpression = expr,
            IteratorName = "item"
        };
        
        var instance = new WorkflowInstance();
        var context = new ExecutionContext(instance, Guid.NewGuid());
        
        var items = new List<object> { "first", "second", "third" };
        _evaluatorMock.Setup(x => x.Evaluate(expr, context)).Returns(items);

        var indexKey = $"$loop_{nodeId}_index";

        // Iteration 1
        var result1 = await _executor.ExecuteAsync(node, context);
        Assert.True(result1.IsSuccess);
        Assert.Equal(PortNames.Body, result1.ActivatedPortIds[0]);
        Assert.Equal("first", context.GetState("item"));
        Assert.Equal(1, context.GetState(indexKey));

        // Iteration 2
        var result2 = await _executor.ExecuteAsync(node, context);
        Assert.True(result2.IsSuccess);
        Assert.Equal(PortNames.Body, result2.ActivatedPortIds[0]);
        Assert.Equal("second", context.GetState("item"));
        Assert.Equal(2, context.GetState(indexKey));

        // Iteration 3
        var result3 = await _executor.ExecuteAsync(node, context);
        Assert.True(result3.IsSuccess);
        Assert.Equal(PortNames.Body, result3.ActivatedPortIds[0]);
        Assert.Equal("third", context.GetState("item"));
        Assert.Equal(3, context.GetState(indexKey));

        // Iteration 4 (End)
        var result4 = await _executor.ExecuteAsync(node, context);
        Assert.True(result4.IsSuccess);
        Assert.Equal(PortNames.Completed, result4.ActivatedPortIds[0]);
        
        // Ensure state is cleaned up
        Assert.Null(context.GetState("item"));
        Assert.Null(context.GetState(indexKey));
    }
}
