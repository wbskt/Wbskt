using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class ForEachNodeExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_forks_one_child_per_item()
    {
        var executor = new ForEachNodeExecutor(new ExpressionEvaluator());
        NodeContext context = CreateContext(new ForEachNode { NodeId = Guid.NewGuid(), Name = "foreach", Ports = CreatePorts(), Config = new ForEachConfig { Collection = "items" } }, new Dictionary<string, JsonElement>
        {
            ["items"] = JsonSerializer.SerializeToElement(new[] { "a", "b", "c" })
        });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var fork = Assert.IsType<NodeExecutionResult.Fork>(result);
        Assert.Equal("done", fork.ContinueNodeId);
        Assert.Equal(3, fork.Children.Count);
        Assert.All(fork.Children, child => Assert.Equal("body", child.NodeId));
        Assert.Equal("a", fork.Children.ElementAt(0).LocalState["item"].GetString());
        Assert.Equal("b", fork.Children.ElementAt(1).LocalState["item"].GetString());
        Assert.Equal("c", fork.Children.ElementAt(2).LocalState["item"].GetString());
    }

    [Fact]
    public async Task ExecuteAsync_empty_collection_returns_empty_fork()
    {
        var executor = new ForEachNodeExecutor(new ExpressionEvaluator());
        NodeContext context = CreateContext(new ForEachNode { NodeId = Guid.NewGuid(), Name = "foreach", Ports = CreatePorts(), Config = new ForEachConfig { Collection = "items" } }, new Dictionary<string, JsonElement>
        {
            ["items"] = JsonSerializer.SerializeToElement(Array.Empty<string>())
        });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var fork = Assert.IsType<NodeExecutionResult.Fork>(result);
        Assert.Equal("done", fork.ContinueNodeId);
        Assert.Empty(fork.Children);
    }

    private static NodeContext CreateContext(ForEachNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow),
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static IReadOnlyCollection<PortDefinition> CreatePorts()
    {
        return [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "body", Direction = PortDirection.Output, Label = "Body" },
            new PortDefinition { PortId = "done", Direction = PortDirection.Output, Label = "Done" }
        ];
    }

    private sealed class StubProviderComposite : IProviderComposite
    {
        public IWorkflowDefinitionProvider WorkflowDefinition => throw new NotSupportedException();
        public ITriggerRegistrationProvider TriggerRegistration => throw new NotSupportedException();
        public IBookmarkProvider Bookmark => throw new NotSupportedException();
        public ISharedVariableProvider SharedVariable => throw new NotSupportedException();
        public IIdempotencyKeyProvider IdempotencyKey => throw new NotSupportedException();
        public IPendingTriggerEventProvider PendingTriggerEvent => throw new NotSupportedException();
        public IScheduledFireProvider ScheduledFire => throw new NotSupportedException();
    }
}
