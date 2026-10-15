using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Runtime.BranchResults;
using static Wbskt.Host.Tests.Runtime.BranchResults.BranchResultTestData;

namespace Wbskt.Host.Tests.Runtime.BranchResults;

/// <summary>
/// A Fork branch that ends before its Join used to never count there: a Mode=All join never
/// converged, the other branches ended as "not the last", the run finalized Succeeded and the work
/// after the Join was skipped without a trace. These drive the real End and Join executors through
/// the real result handlers, against an aggregator that keeps the procedure's rules.
/// </summary>
public sealed class JoinDeclineTests
{
    private static readonly Guid ForkId = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid EndId = Guid.Parse("20000000-0000-0000-0000-000000000000");
    private static readonly Guid StepId = Guid.Parse("30000000-0000-0000-0000-000000000000");
    private static readonly Guid JoinId = Guid.Parse("40000000-0000-0000-0000-000000000000");
    private static readonly Guid AfterJoinId = Guid.Parse("50000000-0000-0000-0000-000000000000");
    private static readonly Guid Token = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid EndingBranchRefId = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000a");
    private static readonly Guid JoiningBranchRefId = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private readonly List<string> _log = [];
    private readonly RecordingRunJournal _journal;
    private readonly Mock<IRunCancellationService> _cancellation = new();

    public JoinDeclineTests()
    {
        _journal = new RecordingRunJournal(_log);
        _cancellation
            .Setup(cancellation => cancellation.FailRunAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((long _, string reason, CancellationToken _) => _log.Add($"fail-run:{reason}"))
            .ReturnsAsync(true);
    }

    // fork: a -> end, b -> step -> join -> afterJoin
    private static WorkflowDefinition Graph(JoinMode mode) => Definition(
        [Node(ForkId), new EndNode { NodeId = EndId, Name = "end", Ports = [] }, Node(StepId), Join(mode), Node(AfterJoinId)],
        [
            new Edge((ForkId, "a"), (EndId, "in")),
            new Edge((ForkId, "b"), (StepId, "in")),
            new Edge((StepId, "default"), (JoinId, "in")),
            new Edge((JoinId, "default"), (AfterJoinId, "in"))
        ]);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_All_join_with_a_branch_that_ended_fails_the_run_instead_of_skipping_past_the_join(bool endingBranchFirst)
    {
        var aggregator = new CohortAggregator(expected: 2, JoinMode.All, JoinId);
        WorkflowDefinition graph = Graph(JoinMode.All);

        if (endingBranchFirst)
        {
            await EndAtEndNodeAsync(aggregator, graph);
            await ArriveAtJoinAsync(aggregator, graph);
        }
        else
        {
            await ArriveAtJoinAsync(aggregator, graph);
            await EndAtEndNodeAsync(aggregator, graph);
        }

        // Nobody continued past the Join, and the run is failed rather than left to finalize Succeeded.
        Assert.Equal(0, aggregator.ContinuationsGranted);
        Assert.Single(_log, entry => entry.StartsWith("fail-run:", StringComparison.Ordinal) && entry.Contains(JoinDeclines.ErrorCode, StringComparison.Ordinal));
        Assert.DoesNotContain(_log, entry => entry.Contains(AfterJoinId.ToString(), StringComparison.Ordinal));

        RunJournalEntry abandoned = Assert.Single(_journal.Entries, entry => entry.EventKind == HistoryEventKind.JoinAbandoned);
        Assert.Equal(EndingBranchRefId, abandoned.BranchRefId);
        Assert.Equal(EndId, abandoned.NodeId);
        Assert.True(JsonSerializer.SerializeToElement(abandoned.Payload).GetProperty("joinFailed").GetBoolean());
        Assert.Equal(HistoryEventKind.SeverityWarn, HistoryEventKind.SeverityFor(HistoryEventKind.JoinAbandoned));
    }

    [Fact]
    public async Task The_decline_and_the_run_failure_come_before_the_branch_is_released()
    {
        var aggregator = new CohortAggregator(expected: 2, JoinMode.All, JoinId);

        await EndAtEndNodeAsync(aggregator, Graph(JoinMode.All));

        Assert.Equal(
            new[]
            {
                $"journal:{HistoryEventKind.JoinAbandoned}",
                $"fail-run:Join {JoinId} cannot converge ({JoinDeclines.ErrorCode}): branch {EndingBranchRefId} ended at node {EndId} without reaching it",
                "completed",
                $"journal:{HistoryEventKind.BranchCompleted}",
                $"release:{RunId}:1"
            },
            _log);
    }

    [Fact]
    public async Task An_Any_join_carries_on_without_the_branch_that_ended()
    {
        var aggregator = new CohortAggregator(expected: 2, JoinMode.Any, JoinId);
        WorkflowDefinition graph = Graph(JoinMode.Any);

        await EndAtEndNodeAsync(aggregator, graph);
        NodeExecutionResult joined = await ArriveAtJoinAsync(aggregator, graph);

        Assert.IsType<NodeExecutionResult.Continue>(joined);
        Assert.Equal(1, aggregator.ContinuationsGranted);
        Assert.DoesNotContain(_log, entry => entry.StartsWith("fail-run:", StringComparison.Ordinal));
        RunJournalEntry abandoned = Assert.Single(_journal.Entries, entry => entry.EventKind == HistoryEventKind.JoinAbandoned);
        Assert.False(JsonSerializer.SerializeToElement(abandoned.Payload).GetProperty("joinFailed").GetBoolean());
    }

    [Fact]
    public async Task A_branch_that_ends_through_a_port_with_no_edge_declines_its_join()
    {
        var aggregator = new CohortAggregator(expected: 2, JoinMode.All, JoinId);
        var branches = new RecordingBranchProvider(_log, CohortBranch(StepId, EndingBranchRefId, 2001));
        var handler = new ContinueResultHandler(branches, new BranchEndings(branches, _journal, new RecordingBranchTerminator(_log)), new JoinDeclines(aggregator, _cancellation.Object, _journal));

        BranchResultOutcome outcome = await handler.HandleAsync(
            new NodeExecutionResult.Continue("nowhere", new Dictionary<string, JsonElement>()),
            Context(Node(StepId), Graph(JoinMode.All), branches.Current),
            CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(new[] { (2001L, "declined") }, aggregator.Contributions);
        Assert.Contains(_log, entry => entry.StartsWith("fail-run:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_winner_of_an_outermost_join_whose_port_leads_nowhere_declines_nothing()
    {
        // The Join removes the closed cohort's token, so the winner has no cohort left to decline.
        var aggregator = new CohortAggregator(expected: 1, JoinMode.All, JoinId);
        WorkflowDefinition graph = Definition([Node(StepId), Join(JoinMode.All)], [new Edge((StepId, "default"), (JoinId, "in"))]);

        NodeExecutionResult result = await ArriveAtJoinAsync(aggregator, graph);
        var branches = new RecordingBranchProvider(_log, CohortBranch(JoinId, JoiningBranchRefId, 2002));
        await new ContinueResultHandler(branches, new BranchEndings(branches, _journal, new RecordingBranchTerminator(_log)), new JoinDeclines(aggregator, _cancellation.Object, _journal))
            .HandleAsync((NodeExecutionResult.Continue)result, Context(Join(JoinMode.All), graph, branches.Current), CancellationToken.None);

        Assert.Equal(new[] { (2002L, "succeeded") }, aggregator.Contributions);
        Assert.DoesNotContain(_journal.Entries, entry => entry.EventKind == HistoryEventKind.JoinAbandoned);
    }

    [Fact]
    public async Task A_branch_outside_any_cohort_declines_nothing()
    {
        var aggregator = new CohortAggregator(expected: 2, JoinMode.All, JoinId);
        var branches = new RecordingBranchProvider(_log, Branch(EndId));
        var handler = new TerminalResultHandler(new BranchEndings(branches, _journal, new RecordingBranchTerminator(_log)), new JoinDeclines(aggregator, _cancellation.Object, _journal));

        await handler.HandleAsync(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed), Context(Node(EndId), Graph(JoinMode.All), branches.Current), CancellationToken.None);

        Assert.Empty(aggregator.Contributions);
        Assert.Equal(new[] { "completed", $"journal:{HistoryEventKind.BranchCompleted}", $"release:{RunId}:1" }, _log);
    }

    private async Task EndAtEndNodeAsync(CohortAggregator aggregator, WorkflowDefinition graph)
    {
        var branches = new RecordingBranchProvider(_log, CohortBranch(EndId, EndingBranchRefId, 2001));
        BaseNode end = graph.Nodes.Single(node => node.NodeId == EndId);
        BranchResultContext context = Context(end, graph, branches.Current);

        NodeExecutionResult result = await new EndNodeExecutor().ExecuteAsync(NodeContextFor(context, 2001), CancellationToken.None);
        await new TerminalResultHandler(new BranchEndings(branches, _journal, new RecordingBranchTerminator(_log)), new JoinDeclines(aggregator, _cancellation.Object, _journal))
            .HandleAsync((NodeExecutionResult.Terminal)result, context, CancellationToken.None);
    }

    private async Task<NodeExecutionResult> ArriveAtJoinAsync(CohortAggregator aggregator, WorkflowDefinition graph)
    {
        var branches = new RecordingBranchProvider(_log, CohortBranch(JoinId, JoiningBranchRefId, 2002));
        BaseNode join = graph.Nodes.Single(node => node.NodeId == JoinId);
        BranchResultContext context = Context(join, graph, branches.Current);

        NodeExecutionResult result = await new JoinNodeExecutor(aggregator).ExecuteAsync(NodeContextFor(context, 2002), CancellationToken.None);
        if (result is NodeExecutionResult.Terminal terminal)
        {
            // Not the last to arrive: the Join has counted this branch, and ending it here declines nothing.
            await new TerminalResultHandler(new BranchEndings(branches, _journal, new RecordingBranchTerminator(_log)), new JoinDeclines(aggregator, _cancellation.Object, _journal))
                .HandleAsync(terminal, context, CancellationToken.None);
        }

        return result;
    }

    private static BranchRow CohortBranch(Guid nodeId, Guid refId, int id) =>
        Branch(nodeId, $$"""{"{{JoinTokens.TokenKey}}":"{{Token}}"}""") with { Id = id, RefId = refId };

    private static NodeContext NodeContextFor(BranchResultContext context, long branchId) => new()
    {
        Branch = context.NodeContext with { BranchId = branchId },
        Node = context.Node,
        CancellationToken = CancellationToken.None,
        IdempotencyKey = "test-key",
        Definition = context.Definition
    };

    private static JoinNode Join(JoinMode mode) => new() { NodeId = JoinId, Name = "join", Ports = [], Config = new JoinConfig { Mode = mode } };

    /// <summary>One cohort, counted by the rules of dbo.JoinAggregator_Contribute.</summary>
    private sealed class CohortAggregator(int expected, JoinMode mode, Guid joinNodeId) : IJoinAggregatorProvider
    {
        private readonly HashSet<long> _seen = [];
        private int _expected = expected;
        private int _contributed;
        private int _succeeded;
        private int _failed;
        private bool _claimed;

        public List<(long BranchId, string Outcome)> Contributions { get; } = [];

        public int ContinuationsGranted { get; private set; }

        public Task InitializeAsync(Guid joinToken, int runId, int expectedCount, string mode, int quorumCount, Guid? joinNodeId, CancellationToken ct) => Task.CompletedTask;

        public Task<JoinContributionResult> ContributeAsync(Guid joinToken, long branchId, string outcome, CancellationToken ct)
        {
            Assert.Equal(Token, joinToken);
            Contributions.Add((branchId, outcome));
            bool repeat = !_seen.Add(branchId);
            bool declined = outcome == "declined";
            bool abandoned = false;
            bool shouldContinue = false;

            if (!repeat)
            {
                if (declined)
                {
                    _expected--;
                }
                else
                {
                    _contributed++;
                    _succeeded += outcome == "succeeded" ? 1 : 0;
                    _failed += outcome == "failed" ? 1 : 0;
                }
            }

            if (declined)
            {
                abandoned = !repeat && !_claimed && (mode == JoinMode.All || (mode == JoinMode.Any && _contributed == 0 && _expected <= 0));
                _claimed |= abandoned;
            }
            else
            {
                bool met = mode == JoinMode.All ? _contributed >= _expected : _contributed >= 1;
                if (met && !_claimed && !repeat)
                {
                    _claimed = true;
                    shouldContinue = true;
                    ContinuationsGranted++;
                }
            }

            return Task.FromResult(new JoinContributionResult(shouldContinue, _contributed, _succeeded, _failed, _expected, joinNodeId, abandoned));
        }

        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => Task.CompletedTask;
    }
}
