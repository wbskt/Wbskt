using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class NodeExecutionResultTests
{
    [Fact]
    public void Continue_carries_outbound_port()
    {
        // Arrange
        var patch = new Dictionary<string, JsonElement>
        {
            ["status"] = JsonDocument.Parse("\"ok\"").RootElement.Clone()
        };

        // Act
        NodeExecutionResult result = new NodeExecutionResult.Continue("next", patch);

        // Assert
        var @continue = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("next", @continue.OutboundPort);
        Assert.Same(patch, @continue.LocalStatePatch);
    }

    [Fact]
    public void Fork_with_two_children()
    {
        // Arrange
        var childOneState = new Dictionary<string, JsonElement>
        {
            ["index"] = JsonDocument.Parse("1").RootElement.Clone()
        };
        var childTwoState = new Dictionary<string, JsonElement>
        {
            ["index"] = JsonDocument.Parse("2").RootElement.Clone()
        };
        var children = new[]
        {
            new ForkSpec("child-1", childOneState),
            new ForkSpec("child-2", childTwoState)
        };
        var patch = new Dictionary<string, JsonElement>
        {
            ["forked"] = JsonDocument.Parse("true").RootElement.Clone()
        };

        // Act
        NodeExecutionResult result = new NodeExecutionResult.Fork(children, "continue-node", patch);

        // Assert
        var fork = Assert.IsType<NodeExecutionResult.Fork>(result);
        Assert.Equal(2, fork.Children.Count);
        Assert.Equal("continue-node", fork.ContinueNodeId);
        Assert.Same(children, fork.Children);
        Assert.Same(patch, fork.LocalStatePatch);
    }

    [Fact]
    public void WaitForBookmark_carries_condition()
    {
        // Arrange
        WakeCondition condition = new SignalWakeCondition("operator-ack", "corr-1");
        var patch = new Dictionary<string, JsonElement>
        {
            ["waiting"] = JsonDocument.Parse("true").RootElement.Clone()
        };

        // Act
        NodeExecutionResult result = new NodeExecutionResult.WaitForBookmark(condition, patch);

        // Assert
        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        Assert.Same(condition, wait.Condition);
        Assert.Same(patch, wait.LocalStatePatch);
    }

    [Fact]
    public void Fail_with_retryable_flag()
    {
        // Arrange
        var cause = new InvalidOperationException("boom");

        // Act
        NodeExecutionResult result = new NodeExecutionResult.Fail("E_TIMEOUT", "Connection timeout", true, cause);

        // Assert
        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("E_TIMEOUT", fail.ErrorCode);
        Assert.Equal("Connection timeout", fail.Message);
        Assert.True(fail.Retryable);
        Assert.Same(cause, fail.Cause);
    }

    [Fact]
    public void Terminal_with_reason()
    {
        // Act
        NodeExecutionResult result = new NodeExecutionResult.Terminal(BranchTerminalReason.Completed);

        // Assert
        var terminal = Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(BranchTerminalReason.Completed, terminal.Reason);
    }

    public static IEnumerable<object[]> ResultCases()
    {
        yield return [new NodeExecutionResult.Continue("next", new Dictionary<string, JsonElement>()), typeof(NodeExecutionResult.Continue)];
        yield return [new NodeExecutionResult.Fork(Array.Empty<ForkSpec>(), null, new Dictionary<string, JsonElement>()), typeof(NodeExecutionResult.Fork)];
        yield return [new NodeExecutionResult.WaitForBookmark(new SignalWakeCondition("ack", "corr"), new Dictionary<string, JsonElement>()), typeof(NodeExecutionResult.WaitForBookmark)];
        yield return [new NodeExecutionResult.Fail("ERR", "message", false, null), typeof(NodeExecutionResult.Fail)];
        yield return [new NodeExecutionResult.Terminal(BranchTerminalReason.Failed), typeof(NodeExecutionResult.Terminal)];
    }

    [Theory]
    [MemberData(nameof(ResultCases))]
    public void Planned_result_types_are_assignable_from_base(NodeExecutionResult result, Type expectedType)
    {
        // Assert
        Assert.IsAssignableFrom<NodeExecutionResult>(result);
        Assert.IsType(expectedType, result);
    }
}
