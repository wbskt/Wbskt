using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class ParallelForEachNodeExecutorTests
{
    private static readonly IExpressionEvaluator Evaluator = new ExpressionEvaluator(new SystemClock());

    [Fact]
    public async Task ExecuteAsync_forks_one_child_per_item_with_join_token()
    {
        var aggregatorMock = CreateAggregatorMock();
        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        var node = CreateNode();
        NodeContext ctx = CreateContext(
            node,
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(new[] { "x", "y", "z" })
            });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fork = Assert.IsType<NodeExecutionResult.Fork>(result);
        Assert.Null(fork.ContinueOutboundPort);
        Assert.Equal(3, fork.Children.Count);
        Assert.All(fork.Children, child => Assert.Equal("body", child.OutboundPort));
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
    public async Task ExecuteAsync_stamps_paired_Join_config_onto_the_aggregator()
    {
        // The Join's mode/quorum are recorded at fan-out so a branch that FAILS can contribute later
        // from BranchLoop, which never sees the Join node.
        var aggregatorMock = CreateAggregatorMock();
        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        var node = CreateNode();
        Guid joinNodeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        NodeContext ctx = CreateContext(
            node,
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(new[] { "a", "b" })
            },
            joinNodeId: joinNodeId,
            joinMode: JoinMode.Quorum,
            quorumCount: 2);

        await executor.ExecuteAsync(ctx, CancellationToken.None);

        aggregatorMock.Verify(
            a => a.InitializeAsync(It.IsAny<Guid>(), 42, 2, "Quorum", 2, joinNodeId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_fail_when_no_Join_is_downstream_of_body()
    {
        // Without a Join the cohort could never converge, so fail loudly rather than fan out into a
        // guaranteed hang. The validator is expected to catch this at publish time too (WF-19).
        var aggregatorMock = CreateAggregatorMock();
        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        var node = CreateNode();
        NodeContext ctx = CreateContext(
            node,
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(new[] { "a" })
            },
            includeJoin: false);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("PFE_NO_JOIN", fail.ErrorCode);
        Assert.False(fail.Retryable);
        aggregatorMock.Verify(
            a => a.InitializeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_empty_collection_returns_continue_empty()
    {
        var aggregatorMock = CreateAggregatorMock();
        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        var node = CreateNode();
        NodeContext ctx = CreateContext(
            node,
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(Array.Empty<string>())
            });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("empty", cont.OutboundPort);

        aggregatorMock.Verify(
            a => a.InitializeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_returns_fail_when_config_is_null()
    {
        var aggregatorMock = CreateAggregatorMock();
        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        var node = new ParallelForEachNode { NodeId = Guid.NewGuid(), Name = "pfe", Ports = CreatePorts(), Config = null };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>());

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("PFE_CONFIG_INVALID", fail.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_returns_fail_when_collection_is_not_array()
    {
        var aggregatorMock = CreateAggregatorMock();
        var executor = new ParallelForEachNodeExecutor(Evaluator, aggregatorMock.Object);
        var node = CreateNode();
        NodeContext ctx = CreateContext(
            node,
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement("not-an-array")
            });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("PFE_COLLECTION_NOT_ARRAY", fail.ErrorCode);
    }

    private static Mock<IJoinAggregatorProvider> CreateAggregatorMock()
    {
        var mock = new Mock<IJoinAggregatorProvider>();
        mock.Setup(a => a.InitializeAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static ParallelForEachNode CreateNode()
    {
        return new ParallelForEachNode
        {
            NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "pfe",
            Ports = CreatePorts(),
            Config = new ParallelForEachConfig { Collection = "items" }
        };
    }

    private static NodeContext CreateContext(
        ParallelForEachNode node,
        IReadOnlyDictionary<string, JsonElement> localState,
        Guid? joinNodeId = null,
        JoinMode joinMode = JoinMode.All,
        int? quorumCount = null,
        bool includeJoin = true)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow, 9),
            Node = node,
            Definition = BuildDefinition(node, joinNodeId ?? Guid.NewGuid(), joinMode, quorumCount, includeJoin),
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None,
            IdempotencyKey = "test-key"
        };
    }

    /// <summary>pfe --body--> join (unless <paramref name="includeJoin"/> is false).</summary>
    private static WorkflowDefinition BuildDefinition(
        ParallelForEachNode node,
        Guid joinNodeId,
        JoinMode joinMode,
        int? quorumCount,
        bool includeJoin)
    {
        var nodes = new List<BaseNode> { node };
        var edges = new List<Edge>();

        if (includeJoin)
        {
            nodes.Add(new JoinNode
            {
                NodeId = joinNodeId,
                Name = "join",
                Ports =
                [
                    new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                    new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }
                ],
                Config = new JoinConfig { Mode = joinMode, QuorumCount = quorumCount }
            });
            edges.Add(new Edge((node.NodeId, "body"), (joinNodeId, "in")));
        }

        return new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "pfe-test",
            null,
            true,
            nodes,
            edges,
            [],
            DateTime.UtcNow,
            7);
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
