using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class NodeExecutionResultTests
{
    [Fact]
    public void Success_CreatesSuccessResult_WithAllProperties()
    {
        // Arrange
        var output = JsonDocument.Parse("{\"result\":42}").RootElement;

        // Act
        var result = NodeExecutionResult.Success(output, "outPort", skipSave: true);

        // Assert
        Assert.IsType<NodeExecutionResult.SuccessResult>(result);
        var success = (NodeExecutionResult.SuccessResult)result;
        Assert.Equal(42, success.Output!.Value.GetProperty("result").GetInt32());
        Assert.Equal("outPort", success.TakePort);
        Assert.True(success.SkipSave);
    }

    [Fact]
    public void Success_HandlesNullOutput()
    {
        // Act
        var result = NodeExecutionResult.Success(null, null, skipSave: false);

        // Assert
        var success = (NodeExecutionResult.SuccessResult)result;
        Assert.Null(success.Output);
        Assert.Null(success.TakePort);
        Assert.False(success.SkipSave);
    }

    [Fact]
    public void Fork_CreatesForkResult_WithChildren()
    {
        // Arrange
        var child1 = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new System.Text.Json.Nodes.JsonObject()
        };
        var child2 = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new System.Text.Json.Nodes.JsonObject()
        };
        var children = new[] { child1, child2 };

        // Act
        var result = NodeExecutionResult.Fork(children, "cohort-123");

        // Assert
        Assert.IsType<NodeExecutionResult.ForkResult>(result);
        var fork = (NodeExecutionResult.ForkResult)result;
        Assert.Equal(2, fork.Children.Count);
        Assert.Equal("cohort-123", fork.CohortId);
    }

    [Fact]
    public void Bookmark_CreatesBookmarkResult_WithAllProperties()
    {
        // Arrange
        var state = JsonDocument.Parse("{\"waitFor\":\"deviceA\"}").RootElement;

        // Act
        var result = NodeExecutionResult.Bookmark("signal:device-connected", TimeSpan.FromMinutes(5), "timeout", state);

        // Assert
        Assert.IsType<NodeExecutionResult.BookmarkResult>(result);
        var bookmark = (NodeExecutionResult.BookmarkResult)result;
        Assert.Equal("signal:device-connected", bookmark.MatchKey);
        Assert.Equal(TimeSpan.FromMinutes(5), bookmark.Ttl);
        Assert.Equal("timeout", bookmark.TtlPort);
        Assert.NotNull(bookmark.State);
        Assert.Equal("deviceA", bookmark.State!.Value.GetProperty("waitFor").GetString());
    }

    [Fact]
    public void Bookmark_HandlesNullableFields()
    {
        // Act
        var result = NodeExecutionResult.Bookmark("signal:any", null, null, null);

        // Assert
        var bookmark = (NodeExecutionResult.BookmarkResult)result;
        Assert.Equal("signal:any", bookmark.MatchKey);
        Assert.Null(bookmark.Ttl);
        Assert.Null(bookmark.TtlPort);
        Assert.Null(bookmark.State);
    }

    [Fact]
    public void Fail_CreatesFailResult_WithAllProperties()
    {
        // Arrange
        var details = JsonDocument.Parse("{\"statusCode\":500}").RootElement;

        // Act
        var result = NodeExecutionResult.Fail("Connection timeout", "E_TIMEOUT", details);

        // Assert
        Assert.IsType<NodeExecutionResult.FailResult>(result);
        var fail = (NodeExecutionResult.FailResult)result;
        Assert.Equal("Connection timeout", fail.ErrorMessage);
        Assert.Equal("E_TIMEOUT", fail.ErrorCode);
        Assert.NotNull(fail.ErrorDetails);
        Assert.Equal(500, fail.ErrorDetails!.Value.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public void Fail_HandlesNullableFields()
    {
        // Act
        var result = NodeExecutionResult.Fail("Unknown error", null, null);

        // Assert
        var fail = (NodeExecutionResult.FailResult)result;
        Assert.Equal("Unknown error", fail.ErrorMessage);
        Assert.Null(fail.ErrorCode);
        Assert.Null(fail.ErrorDetails);
    }

    [Fact]
    public void Terminal_CreatesTerminalResult()
    {
        // Act
        var result = NodeExecutionResult.Terminal();

        // Assert
        Assert.IsType<NodeExecutionResult.TerminalResult>(result);
    }

    [Fact]
    public void Compensation_CreatesCompensationResult_WithBranchIds()
    {
        // Arrange
        var branchIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Act
        var result = NodeExecutionResult.Compensation(branchIds);

        // Assert
        Assert.IsType<NodeExecutionResult.CompensationResult>(result);
        var compensation = (NodeExecutionResult.CompensationResult)result;
        Assert.Equal(3, compensation.BranchesToCompensate.Count);
    }

    [Fact]
    public void AllResultTypes_InheritFromBaseClass()
    {
        // Arrange & Act
        var results = new NodeExecutionResult[]
        {
            NodeExecutionResult.Success(null, null, false),
            NodeExecutionResult.Fork(Array.Empty<BranchContext>(), null),
            NodeExecutionResult.Bookmark("key", null, null, null),
            NodeExecutionResult.Fail("error", null, null),
            NodeExecutionResult.Terminal(),
            NodeExecutionResult.Compensation(Array.Empty<Guid>())
        };

        // Assert
        foreach (var result in results)
        {
            Assert.IsAssignableFrom<NodeExecutionResult>(result);
        }
    }
}
