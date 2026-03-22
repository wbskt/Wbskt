using Moq;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Services.NodeExecutors;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services.NodeExecutors;

public class LogicGateExecutorTests
{
    private readonly Mock<IWorkflowExpressionEvaluator> _evaluatorMock;
    private readonly LogicGateExecutor _executor;

    public LogicGateExecutorTests()
    {
        _evaluatorMock = new Mock<IWorkflowExpressionEvaluator>();
        _executor = new LogicGateExecutor(_evaluatorMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ConditionIsTrue_ReturnsMatchPort()
    {
        // Arrange
        var condition = new LiteralExpression { Value = null }; // Dummy condition since we mock it
        var node = new LogicGateNode
        {
            NodeId = Guid.NewGuid(),
            Condition = condition
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());
        
        _evaluatorMock.Setup(x => x.Evaluate(condition, context)).Returns(true);

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Match, result.ActivatedPortIds.First());
    }

    [Fact]
    public async Task ExecuteAsync_ConditionIsFalse_ReturnsOtherwisePort()
    {
        // Arrange
        var condition = new LiteralExpression { Value = null };
        var node = new LogicGateNode
        {
            NodeId = Guid.NewGuid(),
            Condition = condition
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());
        
        _evaluatorMock.Setup(x => x.Evaluate(condition, context)).Returns(false);

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Otherwise, result.ActivatedPortIds.First());
    }

    [Fact]
    public async Task ExecuteAsync_ConditionReturnsNonBoolean_ReturnsOtherwisePort()
    {
        // Arrange
        var condition = new LiteralExpression { Value = null };
        var node = new LogicGateNode
        {
            NodeId = Guid.NewGuid(),
            Condition = condition
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());
        
        // Return a string instead of boolean
        _evaluatorMock.Setup(x => x.Evaluate(condition, context)).Returns("TruthyString");

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Otherwise, result.ActivatedPortIds.First());
    }
}
