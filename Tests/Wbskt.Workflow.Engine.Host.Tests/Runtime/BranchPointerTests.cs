using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class BranchPointerTests
{
    [Fact]
    public void From_extracts_pointer_fields()
    {
        // Arrange
        var localState = new Dictionary<string, JsonElement>
        {
            ["step"] = JsonDocument.Parse("3").RootElement.Clone()
        };
        var context = new BranchContext(
            11,
            22,
            33,
            Guid.NewGuid(),
            2,
            "node-42",
            7,
            localState,
            new Dictionary<string, JsonElement>(),
            "corr-42",
            new DateTime(2026, 5, 26, 13, 0, 0, DateTimeKind.Utc),
            9);

        // Act
        var pointer = BranchPointer.From(context);

        // Assert
        Assert.Equal("node-42", pointer.NodeId);
        Assert.Equal(7, pointer.Attempt);
        Assert.Same(localState, pointer.LocalState);
    }
}
