using Moq;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Services.NodeExecutors;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services.NodeExecutors;

public class VariableExecutorTests
{
    private readonly Mock<IWorkflowExpressionEvaluator> _evaluatorMock;
    private readonly VariableExecutor _executor;

    public VariableExecutorTests()
    {
        _evaluatorMock = new Mock<IWorkflowExpressionEvaluator>();
        _executor = new VariableExecutor(_evaluatorMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyVariableName_ReturnsFail()
    {
        // Arrange
        var node = new VariableNode
        {
            NodeId = Guid.NewGuid(),
            VariableName = "",
            Operation = VariableOperation.Set
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Variable name is required.", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_SetOperation_SetsValueInStateAndReturnsOutPort()
    {
        // Arrange
        var expr = new LiteralExpression { Value = null };
        var node = new VariableNode
        {
            NodeId = Guid.NewGuid(),
            VariableName = "myVar",
            Operation = VariableOperation.Set,
            Expression = expr
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());
        _evaluatorMock.Setup(x => x.Evaluate(expr, context)).Returns("TestValue");

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Out, result.ActivatedPortIds[0]);
        Assert.Equal("TestValue", context.GetState("myVar"));
    }

    [Fact]
    public async Task ExecuteAsync_GetOperation_LoadsFromStateToLastOutput()
    {
        // Arrange
        var node = new VariableNode
        {
            NodeId = Guid.NewGuid(),
            VariableName = "myVar",
            Operation = VariableOperation.Get
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());
        context.SetState("myVar", 123.45);

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(123.45, context.LastNodeOutput);
    }

    [Fact]
    public async Task ExecuteAsync_IncrementOperation_IncrementsValueInState()
    {
        // Arrange
        var expr = new LiteralExpression { Value = null };
        var node = new VariableNode
        {
            NodeId = Guid.NewGuid(),
            VariableName = "counter",
            Operation = VariableOperation.Increment,
            Expression = expr
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());
        context.SetState("counter", 5.0);
        
        _evaluatorMock.Setup(x => x.Evaluate(expr, context)).Returns(2.5);

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(7.5, context.GetState("counter"));
    }
    
    [Fact]
    public async Task ExecuteAsync_DecrementOperation_DecrementsValueInState()
    {
        // Arrange
        var expr = new LiteralExpression { Value = null };
        var node = new VariableNode
        {
            NodeId = Guid.NewGuid(),
            VariableName = "counter",
            Operation = VariableOperation.Decrement,
            Expression = expr
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());
        context.SetState("counter", 10.0);
        
        _evaluatorMock.Setup(x => x.Evaluate(expr, context)).Returns(3.0);

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(7.0, context.GetState("counter"));
    }
}
