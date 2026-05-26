using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class NodeExecutorRegistryTests
{
    [Fact]
    public void For_returns_registered_executor()
    {
        // Arrange
        var expected = new StubNodeExecutor(NodeKind.ControlLogic);
        var registry = new NodeExecutorRegistry([expected]);

        // Act
        var actual = registry.For(NodeKind.ControlLogic);

        // Assert
        Assert.Same(expected, actual);
    }

    [Fact]
    public void For_throws_when_unknown()
    {
        // Arrange
        var registry = new NodeExecutorRegistry([new StubNodeExecutor(NodeKind.ControlLogic)]);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => registry.For(NodeKind.ActionEmail));

        // Assert
        Assert.Equal($"No executor registered for {NodeKind.ActionEmail}", exception.Message);
    }

    [Fact]
    public void Constructor_throws_on_duplicate_NodeKind()
    {
        // Arrange
        var first = new StubNodeExecutor(NodeKind.ControlLogic);
        var second = new StubNodeExecutor(NodeKind.ControlLogic);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => new NodeExecutorRegistry([first, second]));

        // Assert
        Assert.Equal($"Duplicate executor for {NodeKind.ControlLogic}", exception.Message);
    }

    private sealed class StubNodeExecutor(string kind) : INodeExecutor
    {
        public string Kind { get; } = kind;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        }
    }
}
