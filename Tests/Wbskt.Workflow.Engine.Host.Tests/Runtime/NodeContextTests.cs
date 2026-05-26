using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class NodeContextTests
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
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
        var node = new TestNode(Guid.NewGuid(), "test", Array.Empty<PortDefinition>());
        var providers = new Mock<IProviderComposite>().Object;
        var cts = new CancellationTokenSource();

        // Act
        var context = new NodeContext
        {
            Branch = branch,
            Node = node,
            Providers = providers,
            Tick = 1,
            ParentResults = null,
            CancellationToken = cts.Token
        };

        // Assert - verify init-only properties compile and are set
        Assert.Same(branch, context.Branch);
        Assert.Same(node, context.Node);
        Assert.Same(providers, context.Providers);
        Assert.Equal(1, context.Tick);
        Assert.Null(context.ParentResults);
        Assert.Equal(cts.Token, context.CancellationToken);
    }

    [Fact]
    public void NodeContext_CanBeCreatedWithParentResults()
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
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
        var node = new TestNode(Guid.NewGuid(), "test", Array.Empty<PortDefinition>());
        var providers = new Mock<IProviderComposite>().Object;
        var parentResults = new Dictionary<string, JsonElement>();

        // Act
        var context = new NodeContext
        {
            Branch = branch,
            Node = node,
            Providers = providers,
            Tick = 2,
            ParentResults = parentResults,
            CancellationToken = CancellationToken.None
        };

        // Assert
        Assert.NotNull(context.ParentResults);
        Assert.Same(parentResults, context.ParentResults);
    }
}
