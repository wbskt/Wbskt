using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Host.Tests.Runtime;

public sealed class NodeContextTests
{
    private sealed record TestNode : BaseNode { public required string KindValue { get; init; } public override string Kind => KindValue; }

    [Fact]
    public void NodeContext_IsImmutable()
    {
        // Arrange
        var branch = new BranchContext(
            1,
            2,
            3,
            Guid.NewGuid(),
            4,
            "node-a",
            5,
            new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(),
            "corr-1",
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            9);
        var node = new TestNode { NodeId = Guid.NewGuid(), Name = "test", Ports = Array.Empty<PortDefinition>(), KindValue = "test" };
        var cts = new CancellationTokenSource();

        // Act
        var context = new NodeContext
        {
            Branch = branch,
            Node = node,
            CancellationToken = cts.Token,
            IdempotencyKey = "test-key"
        };

        // Assert - verify init-only properties compile and are set
        Assert.Same(branch, context.Branch);
        Assert.Same(node, context.Node);
        Assert.Equal(cts.Token, context.CancellationToken);
    }
}
