using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Host.Tests.NodeExecutors.Controls;

public sealed class ForkNodeExecutorTests
{
    private static readonly Guid OuterFork = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid InnerFork = Guid.Parse("20000000-0000-0000-0000-000000000000");
    private static readonly Guid InnerJoin = Guid.Parse("30000000-0000-0000-0000-000000000000");
    private static readonly Guid OuterJoin = Guid.Parse("40000000-0000-0000-0000-000000000000");

    /// <summary>
    /// The nearest Join downstream of an outer Fork whose branch holds a nested Fork is the inner
    /// Join. Stamping that onto the outer cohort's aggregator made the outer cohort converge by the
    /// inner Join's mode and spawn a failed branch's continuation past the wrong Join.
    /// </summary>
    [Fact]
    public async Task An_outer_Fork_stamps_its_own_Join_not_the_nested_one()
    {
        // outer: a -> inner fork (x, y -> inner join [Any] -> outer join), b -> outer join [Quorum 2]
        var definition = Definition(
            [
                Fork(OuterFork, "a", "b"),
                Fork(InnerFork, "x", "y"),
                Join(InnerJoin, JoinMode.Any, null),
                Join(OuterJoin, JoinMode.Quorum, 2)
            ],
            [
                new Edge((OuterFork, "a"), (InnerFork, "in")),
                new Edge((InnerFork, "x"), (InnerJoin, "in")),
                new Edge((InnerFork, "y"), (InnerJoin, "in")),
                new Edge((InnerJoin, "default"), (OuterJoin, "in")),
                new Edge((OuterFork, "b"), (OuterJoin, "in"))
            ]);
        var aggregators = new Mock<IJoinAggregatorProvider>();

        await new ForkNodeExecutor(aggregators.Object).ExecuteAsync(Context(definition, OuterFork), CancellationToken.None);
        await new ForkNodeExecutor(aggregators.Object).ExecuteAsync(Context(definition, InnerFork), CancellationToken.None);

        aggregators.Verify(a => a.InitializeAsync(It.IsAny<Guid>(), 42, 2, "Quorum", 2, OuterJoin, It.IsAny<CancellationToken>()), Times.Once);
        aggregators.Verify(a => a.InitializeAsync(It.IsAny<Guid>(), 42, 2, "Any", 0, InnerJoin, It.IsAny<CancellationToken>()), Times.Once);
        aggregators.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Fork_with_no_Join_at_its_level_stamps_no_Join()
    {
        // Fire-and-forget: a Join further down, past an inner cohort that never converges at this
        // level, is not this Fork's.
        var definition = Definition(
            [Fork(OuterFork, "a", "b"), Fork(InnerFork, "x", "y"), Join(InnerJoin, JoinMode.Any, null)],
            [
                new Edge((OuterFork, "a"), (InnerFork, "in")),
                new Edge((InnerFork, "x"), (InnerJoin, "in")),
                new Edge((InnerFork, "y"), (InnerJoin, "in"))
            ]);
        var aggregators = new Mock<IJoinAggregatorProvider>();

        await new ForkNodeExecutor(aggregators.Object).ExecuteAsync(Context(definition, OuterFork), CancellationToken.None);

        aggregators.Verify(a => a.InitializeAsync(It.IsAny<Guid>(), 42, 2, "All", 0, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static NodeContext Context(WorkflowDefinition definition, Guid forkId)
    {
        BaseNode node = definition.Nodes.Single(n => n.NodeId == forkId);
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, forkId.ToString(), 1, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow, 9),
            Node = node,
            Definition = definition,
            CancellationToken = CancellationToken.None,
            IdempotencyKey = "test-key"
        };
    }

    private static WorkflowDefinition Definition(IReadOnlyCollection<BaseNode> nodes, IReadOnlyCollection<Edge> edges) =>
        new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1, 9, "fork-test", null, true, nodes, edges, [], DateTime.UtcNow, 7);

    private static ForkNode Fork(Guid nodeId, params string[] branches) => new()
    {
        NodeId = nodeId,
        Name = "fork",
        Ports = [],
        Config = new ForkConfig { Branches = [.. branches] }
    };

    private static JoinNode Join(Guid nodeId, JoinMode mode, int? quorum) => new()
    {
        NodeId = nodeId,
        Name = "join",
        Ports = [],
        Config = new JoinConfig { Mode = mode, QuorumCount = quorum }
    };
}
