using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime.BranchResults;
using static Wbskt.Host.Tests.Runtime.BranchResults.BranchResultTestData;

namespace Wbskt.Host.Tests.Runtime.BranchResults;

public sealed class ForkResultHandlerTests
{
    private static readonly Guid ForkNode = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstTarget = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SecondTarget = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AfterFork = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly WorkflowDefinition Graph = Definition(
        [Node(ForkNode), Node(FirstTarget), Node(SecondTarget), Node(AfterFork)],
        [
            new Edge((ForkNode, "a"), (FirstTarget, "in")),
            new Edge((ForkNode, "b"), (SecondTarget, "in")),
            new Edge((ForkNode, "done"), (AfterFork, "in"))
        ]);

    private readonly List<string> _log = [];
    private readonly RecordingRunDispatcher _dispatcher;
    private readonly RecordingBranchProvider _branches;
    private readonly ForkResultHandler _handler;

    public ForkResultHandlerTests()
    {
        _dispatcher = new RecordingRunDispatcher(_log);
        _branches = new RecordingBranchProvider(_log, Branch(ForkNode, """{"shared":1}"""));
        _handler = new ForkResultHandler(
            _branches,
            new RecordingRunCountersProvider(_log),
            _dispatcher,
            new SequentialIdGenerator(),
            new FixedClock(),
            new BranchEndings(_branches, new RecordingRunJournal(_log), new RecordingBranchTerminator(_log)));
    }

    [Fact]
    public async Task A_single_child_with_no_continue_port_is_taken_by_this_branch()
    {
        var fork = new NodeExecutionResult.Fork([new ForkSpec("a", Patch("item", 7))], null, Patch("base", 0));

        BranchResultOutcome outcome = await _handler.HandleAsync(fork, Context(Node(ForkNode), Graph, _branches.Current), CancellationToken.None);

        Assert.Equal(FirstTarget, outcome.Branch!.NodeId);
        Assert.Equal(new[] { $"update:{BranchStatus.Active}:{FirstTarget}" }, _log);
        var localState = LocalState(Assert.Single(_branches.Updates).LocalJson);
        Assert.Equal(1, localState["shared"].GetInt32());
        Assert.Equal(0, localState["base"].GetInt32());
        Assert.Equal(7, localState["item"].GetInt32());
    }

    [Fact]
    public async Task Children_are_counted_then_created_in_one_cohort_and_dispatched_and_the_branch_completes()
    {
        var fork = new NodeExecutionResult.Fork(
            [new ForkSpec("a", Patch("item", 1)), new ForkSpec("b", Patch("item", 2))],
            null,
            Patch("base", 0));

        BranchResultOutcome outcome = await _handler.HandleAsync(fork, Context(Node(ForkNode), Graph, _branches.Current), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(
            new[]
            {
                $"increment:{RunId}:2",
                $"create:{FirstTarget}", "dispatch:5000",
                $"create:{SecondTarget}", "dispatch:5001",
                "completed", $"journal:{HistoryEventKind.BranchCompleted}", $"release:{RunId}:1"
            },
            _log);

        Assert.Equal(2, _branches.Created.Count);
        Assert.Single(_branches.Created.Select(child => child.ForkCohortId).Distinct());
        Assert.NotNull(_branches.Created[0].ForkCohortId);
        Assert.All(_branches.Created, child =>
        {
            Assert.Equal(RunId, child.RunId);
            Assert.Equal(BranchRefId, child.ParentBranchId);
            Assert.Equal(BranchStatus.Active, child.Status);
            Assert.Equal(Now, child.CreatedAt);
        });
        Assert.Equal(2, LocalState(_branches.Created[1].LocalJson)["item"].GetInt32());
        Assert.All(_dispatcher.Requests, request =>
        {
            Assert.Equal((long)RunId, request.RunId);
            Assert.Equal(BranchExecutionReason.ForkChild, request.Reason);
        });
    }

    [Fact]
    public async Task With_a_continue_port_the_branch_moves_on_after_its_children_are_dispatched()
    {
        var fork = new NodeExecutionResult.Fork([new ForkSpec("a", Patch("item", 1))], "done", Patch("base", 0));

        BranchResultOutcome outcome = await _handler.HandleAsync(fork, Context(Node(ForkNode), Graph, _branches.Current), CancellationToken.None);

        Assert.Equal(AfterFork, outcome.Branch!.NodeId);
        Assert.Equal(
            new[] { $"increment:{RunId}:1", $"create:{FirstTarget}", "dispatch:5000", $"update:{BranchStatus.Active}:{AfterFork}" },
            _log);
        var localState = LocalState(Assert.Single(_branches.Updates).LocalJson);
        Assert.Equal(0, localState["base"].GetInt32());
        Assert.False(localState.ContainsKey("item"));
    }

    [Fact]
    public async Task A_Fork_replayed_for_the_same_visit_creates_no_second_cohort()
    {
        // A crash after the children were created and dispatched but before the branch moved past the
        // Fork leaves it on the Fork, and recovery runs the Fork again from the same row version.
        BranchRow atFork = _branches.Current;
        var fork = new NodeExecutionResult.Fork(
            [new ForkSpec("a", Patch("item", 1)), new ForkSpec("b", Patch("item", 2))],
            null,
            Patch("base", 0));
        await _handler.HandleAsync(fork, Context(Node(ForkNode), Graph, atFork), CancellationToken.None);
        _branches.Current = atFork;
        _log.Clear();
        _dispatcher.Requests.Clear();

        BranchResultOutcome outcome = await _handler.HandleAsync(fork, Context(Node(ForkNode), Graph, atFork), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(2, _branches.Created.Count);
        Assert.Empty(_dispatcher.Requests);
        Assert.Equal(
            new[]
            {
                $"increment:{RunId}:2",
                $"exists:{FirstTarget}", $"exists:{SecondTarget}",
                $"decrement:{RunId}:2",
                "completed", $"journal:{HistoryEventKind.BranchCompleted}", $"release:{RunId}:1"
            },
            _log);
    }

    [Fact]
    public async Task A_Fork_cut_short_creates_only_the_missing_children_and_they_join_the_first_cohort()
    {
        // The crash came after the first child was written but before the second; the replayed Fork
        // opened a new join cohort, and the missing child must join the first one with its sibling.
        BranchRow atFork = _branches.Current;
        var firstPass = new NodeExecutionResult.Fork(
            [new ForkSpec("a", Patch(JoinTokens.TokenKey, "first")), new ForkSpec("b", Patch(JoinTokens.TokenKey, "first"))],
            null,
            Patch("base", 0));
        await _handler.HandleAsync(firstPass, Context(Node(ForkNode), Graph, atFork), CancellationToken.None);
        _branches.Forget(_branches.Created[1].RefId);
        _branches.Current = atFork;
        _log.Clear();
        _dispatcher.Requests.Clear();

        var replay = new NodeExecutionResult.Fork(
            [new ForkSpec("a", Patch(JoinTokens.TokenKey, "second")), new ForkSpec("b", Patch(JoinTokens.TokenKey, "second"))],
            null,
            Patch("base", 0));
        await _handler.HandleAsync(replay, Context(Node(ForkNode), Graph, atFork), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                $"increment:{RunId}:2",
                $"exists:{FirstTarget}",
                $"create:{SecondTarget}", "dispatch:5002",
                $"decrement:{RunId}:1",
                "completed", $"journal:{HistoryEventKind.BranchCompleted}", $"release:{RunId}:1"
            },
            _log);
        BranchRow recreated = _branches.Created[2];
        Assert.Equal(_branches.Created[1].RefId, recreated.RefId);
        Assert.Equal(_branches.Created[0].ForkCohortId, recreated.ForkCohortId);
        Assert.Equal("first", LocalState(recreated.LocalJson)[JoinTokens.TokenKey].GetString());
    }

    [Fact]
    public async Task A_later_visit_to_the_same_Fork_opens_a_new_cohort()
    {
        // A Fork inside a loop is visited again after the branch has moved, so its row version differs
        // and the children are new.
        var fork = new NodeExecutionResult.Fork(
            [new ForkSpec("a", Patch("item", 1)), new ForkSpec("b", Patch("item", 2))],
            null,
            Patch("base", 0));
        await _handler.HandleAsync(fork, Context(Node(ForkNode), Graph, _branches.Current), CancellationToken.None);
        _branches.Current = _branches.Current with { Status = BranchStatus.Active, NodeId = ForkNode, RowVersion = [9] };

        await _handler.HandleAsync(fork, Context(Node(ForkNode), Graph, _branches.Current), CancellationToken.None);

        Assert.Equal(4, _branches.Created.Count);
        Assert.Equal(2, _branches.Created.Select(child => child.ForkCohortId).Distinct().Count());
        Assert.Equal(4, _dispatcher.Requests.Count);
    }

    [Fact]
    public async Task A_child_port_that_leads_nowhere_throws()
    {
        var fork = new NodeExecutionResult.Fork([new ForkSpec("missing", Patch("item", 1))], null, Patch("base", 0));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.HandleAsync(fork, Context(Node(ForkNode), Graph, _branches.Current), CancellationToken.None));
    }
}
