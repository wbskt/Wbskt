using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class DefaultCreditCostCalculatorTests
{
    private sealed record TestNode(
        Guid NodeId,
        string Name,
        IReadOnlyCollection<PortDefinition> Ports
    ) : BaseNode(NodeId, Name, Ports)
    {
        public override string Kind => "test";
    }

    [Fact]
    public void Calculate_Returns1_ForAnyNode()
    {
        // Arrange
        var calculator = new DefaultCreditCostCalculator();
        var node = new TestNode(Guid.NewGuid(), "test", Array.Empty<PortDefinition>());
        var result = NodeExecutionResult.Success(null, null, false);

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }

    [Fact]
    public void Calculate_Returns1_ForSuccessResult()
    {
        // Arrange
        var calculator = new DefaultCreditCostCalculator();
        var node = new TestNode(Guid.NewGuid(), "action", Array.Empty<PortDefinition>());
        var result = NodeExecutionResult.Success(null, null, false);

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }

    [Fact]
    public void Calculate_Returns1_ForForkResult()
    {
        // Arrange
        var calculator = new DefaultCreditCostCalculator();
        var node = new TestNode(Guid.NewGuid(), "parallel-foreach", Array.Empty<PortDefinition>());
        var result = NodeExecutionResult.Fork(Array.Empty<BranchContext>(), null);

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }

    [Fact]
    public void Calculate_Returns1_ForBookmarkResult()
    {
        // Arrange
        var calculator = new DefaultCreditCostCalculator();
        var node = new TestNode(Guid.NewGuid(), "wait-for-signal", Array.Empty<PortDefinition>());
        var result = NodeExecutionResult.Bookmark("signal:test", null, null, null);

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }

    [Fact]
    public void Calculate_Returns1_ForFailResult()
    {
        // Arrange
        var calculator = new DefaultCreditCostCalculator();
        var node = new TestNode(Guid.NewGuid(), "action", Array.Empty<PortDefinition>());
        var result = NodeExecutionResult.Fail("error", null, null);

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }

    [Fact]
    public void Calculate_Returns1_ForTerminalResult()
    {
        // Arrange
        var calculator = new DefaultCreditCostCalculator();
        var node = new TestNode(Guid.NewGuid(), "end", Array.Empty<PortDefinition>());
        var result = NodeExecutionResult.Terminal();

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }

    [Fact]
    public void Calculate_Returns1_ForCompensationResult()
    {
        // Arrange
        var calculator = new DefaultCreditCostCalculator();
        var node = new TestNode(Guid.NewGuid(), "compensate", Array.Empty<PortDefinition>());
        var result = NodeExecutionResult.Compensation(Array.Empty<Guid>());

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }
}
