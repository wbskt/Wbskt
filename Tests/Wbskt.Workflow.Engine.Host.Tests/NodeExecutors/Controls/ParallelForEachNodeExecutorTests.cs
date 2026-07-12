using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class ParallelForEachNodeExecutorTests
{
    private static readonly IExpressionEvaluator Evaluator = new ExpressionEvaluator();

    [Fact]
    public async Task ExecuteAsync_forks_one_child_per_item_with_join_token()
    {
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        aggregatorMock
            .Setup(a => a.InitializeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new ParallelForEachNode { NodeId = Guid.NewGuid(), Name = "pfe", Ports = CreatePorts(), Config = new ParallelForEachConfig { Collection = "items" } },
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(new[] { "x", "y", "z" })
            });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fork = Assert.IsType<NodeExecutionResult.Fork>(result);
        Assert.Null(fork.ContinueNodeId);
        Assert.Equal(3, fork.Children.Count);
        Assert.All(fork.Children, child => Assert.Equal("body", child.NodeId));
        Assert.Equal("x", fork.Children.ElementAt(0).LocalState["item"].GetString());
        Assert.Equal("y", fork.Children.ElementAt(1).LocalState["item"].GetString());
        Assert.Equal("z", fork.Children.ElementAt(2).LocalState["item"].GetString());

        // Every child must carry the same join token
        string? token0 = fork.Children.ElementAt(0).LocalState["__join_token"].GetString();
        Assert.NotNull(token0);
        Assert.True(Guid.TryParse(token0, out _));
        Assert.Equal(token0, fork.Children.ElementAt(1).LocalState["__join_token"].GetString());
        Assert.Equal(token0, fork.Children.ElementAt(2).LocalState["__join_token"].GetString());

        // Each child has a unique index
        Assert.Equal(0, fork.Children.ElementAt(0).LocalState["__join_index"].GetInt32());
        Assert.Equal(1, fork.Children.ElementAt(1).LocalState["__join_index"].GetInt32());
        Assert.Equal(2, fork.Children.ElementAt(2).LocalState["__join_index"].GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_initializes_aggregator_with_item_count()
    {
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        aggregatorMock
            .Setup(a => a.InitializeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new ParallelForEachNode { NodeId = Guid.NewGuid(), Name = "pfe", Ports = CreatePorts(), Config = new ParallelForEachConfig { Collection = "items" } },
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(new[] { "a", "b" })
            });

        await executor.ExecuteAsync(ctx, CancellationToken.None);

        aggregatorMock.Verify(
            a => a.InitializeAsync(It.IsAny<Guid>(), 42, 2, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_empty_collection_returns_continue_empty()
    {
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new ParallelForEachNode { NodeId = Guid.NewGuid(), Name = "pfe", Ports = CreatePorts(), Config = new ParallelForEachConfig { Collection = "items" } },
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(Array.Empty<string>())
            });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("empty", cont.OutboundPort);

        aggregatorMock.Verify(
            a => a.InitializeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_returns_fail_when_config_is_null()
    {
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new ParallelForEachNode { NodeId = Guid.NewGuid(), Name = "pfe", Ports = CreatePorts(), Config = null },
            new Dictionary<string, JsonElement>());

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("PFE_CONFIG_INVALID", fail.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_returns_fail_when_collection_is_not_array()
    {
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new ParallelForEachNode { NodeId = Guid.NewGuid(), Name = "pfe", Ports = CreatePorts(), Config = new ParallelForEachConfig { Collection = "items" } },
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement("not-an-array")
            });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("PFE_COLLECTION_NOT_ARRAY", fail.ErrorCode);
    }

    private static NodeContext CreateContext(ParallelForEachNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow, 9),
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
            new PortDefinition { PortId = "empty", Direction = PortDirection.Output, Label = "Empty" }
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
