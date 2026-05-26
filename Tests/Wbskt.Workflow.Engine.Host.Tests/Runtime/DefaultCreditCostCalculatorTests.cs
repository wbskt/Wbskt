using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
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
        NodeExecutionResult result = new NodeExecutionResult.Continue("next", new Dictionary<string, JsonElement>());

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }

    [Fact]
    public void Calculate_Returns1_ForContinueResult()
    {
        // Arrange
        var calculator = new DefaultCreditCostCalculator();
        var node = new TestNode(Guid.NewGuid(), "action", Array.Empty<PortDefinition>());
        NodeExecutionResult result = new NodeExecutionResult.Continue("next", new Dictionary<string, JsonElement>());

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
        NodeExecutionResult result = new NodeExecutionResult.Fork(Array.Empty<ForkSpec>(), null, new Dictionary<string, JsonElement>());

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }

    [Fact]
    public void Calculate_Returns1_ForWaitForBookmarkResult()
    {
        // Arrange
        var calculator = new DefaultCreditCostCalculator();
        var node = new TestNode(Guid.NewGuid(), "wait-for-signal", Array.Empty<PortDefinition>());
        NodeExecutionResult result = new NodeExecutionResult.WaitForBookmark(new SignalWakeCondition("signal:test", "corr-1"), new Dictionary<string, JsonElement>());

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
        NodeExecutionResult result = new NodeExecutionResult.Fail("error", "message", false, null);

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
        NodeExecutionResult result = new NodeExecutionResult.Terminal(BranchTerminalReason.Completed);

        // Act
        var cost = calculator.Calculate(node, result);

        // Assert
        Assert.Equal(1.0m, cost);
    }
}
