using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime.BranchResults;
using static Wbskt.Host.Tests.Runtime.BranchResults.BranchResultTestData;

namespace Wbskt.Host.Tests.Runtime.BranchResults;

public sealed class FailResultHandlerTests
{
    private static readonly Guid FailingNode = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ErrorTarget = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid JoinNode = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AfterJoin = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid JoinToken = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid OuterToken = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid OutermostToken = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly NodeExecutionResult.Fail Boom = new("BOOM", "node blew up", false, null);

    private readonly List<string> _log = [];
    private readonly RecordingRunJournal _journal;
    private readonly RecordingRunDispatcher _dispatcher;
    private readonly Mock<IRunCancellationService> _cancellation = new();
    private readonly Mock<ICompensationOrchestrator> _compensation = new();
    private readonly Mock<IJoinAggregatorProvider> _joins = new();

    public FailResultHandlerTests()
    {
        _journal = new RecordingRunJournal(_log);
        _dispatcher = new RecordingRunDispatcher(_log);
        _cancellation
            .Setup(cancellation => cancellation.FailRunAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((long _, string reason, CancellationToken _) => _log.Add($"fail-run:{reason}"))
            .ReturnsAsync(true);
        _cancellation
            .Setup(cancellation => cancellation.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((long _, string reason, CancellationToken _) => _log.Add($"cancel-run:{reason}"))
            .ReturnsAsync(true);
        _compensation
            .Setup(compensation => compensation.RunAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback(() => _log.Add("compensate"))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task By_default_the_branch_fails_and_is_released()
    {
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));

        BranchResultOutcome outcome = await CreateHandler(branches).HandleAsync(Boom, Context(Node(FailingNode), Definition([Node(FailingNode)], []), branches.Current), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(new[] { "failed", $"journal:{HistoryEventKind.BranchFailed}", $"release:{RunId}:1" }, _log);
        Assert.Contains("BOOM", Assert.Single(branches.Failed).ErrorJson);

        RunJournalEntry branchFailed = Assert.Single(_journal.Entries);
        Assert.Equal(FailingNode, branchFailed.NodeId);
        JsonElement payload = JsonSerializer.SerializeToElement(branchFailed.Payload);
        Assert.Equal("FailBranch", payload.GetProperty("onFailure").GetString());
        Assert.False(payload.GetProperty("compensated").GetBoolean());
        _joins.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failure_OnFailure_turns_into_a_continue_moves_the_branch_on_instead()
    {
        var node = ActionNode(ErrorOutcome.ContinueOnError);
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));
        var definition = Definition([node, Node(ErrorTarget)], [new Edge((FailingNode, "error"), (ErrorTarget, "in"))]);

        BranchResultOutcome outcome = await CreateHandler(branches).HandleAsync(Boom, Context(node, definition, branches.Current), CancellationToken.None);

        Assert.Equal(ErrorTarget, outcome.Branch!.NodeId);
        Assert.Equal(new[] { $"update:{BranchStatus.Active}:{ErrorTarget}" }, _log);
        Assert.True(LocalState(Assert.Single(branches.Updates).LocalJson).ContainsKey("lastError"));
    }

    [Fact]
    public async Task FailRun_fails_the_branch_then_fails_the_run()
    {
        // Failing the run, not cancelling it: the cancel moved the run to 'Cancelling', so a run that
        // failed by its own rule finalized as Cancelled.
        var node = ActionNode(ErrorOutcome.FailRun);
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));

        await CreateHandler(branches).HandleAsync(Boom, Context(node, Definition([node], []), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "failed",
                $"fail-run:Node {FailingNode} failed (BOOM) with onFailure FailRun",
                $"journal:{HistoryEventKind.BranchFailed}",
                $"release:{RunId}:1"
            },
            _log);
        Assert.Contains("BOOM", Assert.Single(branches.Failed).ErrorJson);
        JsonElement payload = JsonSerializer.SerializeToElement(Assert.Single(_journal.Entries).Payload);
        Assert.Equal("FailRun", payload.GetProperty("onFailure").GetString());
        _cancellation.Verify(cancellation => cancellation.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailRun_compensates_first_when_the_workflow_asks_for_it()
    {
        // onFailure FailRun and a control:failRun node are one rule, so both compensate first when
        // the workflow compensates on failure; FailRun used to skip it.
        var node = ActionNode(ErrorOutcome.FailRun);
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));

        await CreateHandler(branches).HandleAsync(Boom, Context(node, Definition([node], [], runCompensationOnFailure: true), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                $"update:{BranchStatus.Compensating}:{FailingNode}",
                "compensate",
                "failed",
                $"fail-run:Node {FailingNode} failed (BOOM) with onFailure FailRun",
                $"journal:{HistoryEventKind.BranchFailed}",
                $"release:{RunId}:1"
            },
            _log);
    }

    [Fact]
    public async Task A_FailRun_node_fails_the_run_with_its_reason()
    {
        // control:failRun has no onFailure, so it used to fail only its own branch: siblings kept
        // running and the run could end PartiallyFailed. It now takes the run-level path.
        var node = FailRun();
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));
        var failRun = new NodeExecutionResult.Fail("FAIL_RUN", "inventory issue", false, null);

        await CreateHandler(branches).HandleAsync(failRun, Context(node, Definition([node], []), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "failed",
                "fail-run:inventory issue",
                $"journal:{HistoryEventKind.BranchFailed}",
                $"release:{RunId}:1"
            },
            _log);
        Assert.Contains("FAIL_RUN", Assert.Single(branches.Failed).ErrorJson);
        JsonElement payload = JsonSerializer.SerializeToElement(Assert.Single(_journal.Entries).Payload);
        Assert.Equal("FailRun", payload.GetProperty("onFailure").GetString());
        _cancellation.Verify(cancellation => cancellation.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_FailRun_node_still_compensates_first_when_the_workflow_asks_for_it()
    {
        var node = FailRun();
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));
        var failRun = new NodeExecutionResult.Fail("FAIL_RUN", "inventory issue", false, null);

        await CreateHandler(branches).HandleAsync(failRun, Context(node, Definition([node], [], runCompensationOnFailure: true), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                $"update:{BranchStatus.Compensating}:{FailingNode}",
                "compensate",
                "failed",
                "fail-run:inventory issue",
                $"journal:{HistoryEventKind.BranchFailed}",
                $"release:{RunId}:1"
            },
            _log);
    }

    [Fact]
    public async Task Compensate_marks_the_branch_compensating_and_compensates_before_failing_it()
    {
        var node = ActionNode(ErrorOutcome.Compensate);
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));

        await CreateHandler(branches).HandleAsync(Boom, Context(node, Definition([node], []), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                $"update:{BranchStatus.Compensating}:{FailingNode}",
                "compensate",
                "failed",
                $"journal:{HistoryEventKind.BranchFailed}",
                $"release:{RunId}:1"
            },
            _log);
        JsonElement payload = JsonSerializer.SerializeToElement(Assert.Single(_journal.Entries).Payload);
        Assert.True(payload.GetProperty("compensated").GetBoolean());
    }

    [Fact]
    public async Task FailFast_fails_the_run()
    {
        // FailFast used to move the run to 'Failing' and nothing else, so a sibling parked on a signal
        // kept the run alive until the signal or its TTL. Failing the run stops that work too.
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));

        await CreateHandler(branches).HandleAsync(Boom, Context(Node(FailingNode), Definition([Node(FailingNode)], [], failFast: true), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "failed",
                $"fail-run:Node {FailingNode} failed (BOOM) on a fail-fast workflow",
                $"journal:{HistoryEventKind.BranchFailed}",
                $"release:{RunId}:1"
            },
            _log);
        _cancellation.Verify(cancellation => cancellation.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailFast_does_not_apply_to_a_compensated_failure()
    {
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));

        await CreateHandler(branches).HandleAsync(Boom, Context(Node(FailingNode), Definition([Node(FailingNode)], [], runCompensationOnFailure: true, failFast: true), branches.Current), CancellationToken.None);

        Assert.DoesNotContain(_log, entry => entry.StartsWith("fail-run:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Running_out_of_credits_on_a_fail_fast_workflow_cancels_the_run_instead_of_failing_it()
    {
        // The cancel alone stops the run's work: the finalizer maps a cancel with this reason to
        // OutOfCredits. Failing the run first stopped the same work twice and journalled both a
        // failure and a cancel for one event.
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));
        var outOfCredits = new NodeExecutionResult.Fail("OUT_OF_CREDITS", "budget spent", false, null);

        await CreateHandler(branches).HandleAsync(outOfCredits, Context(Node(FailingNode), Definition([Node(FailingNode)], [], failFast: true), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "failed",
                "cancel-run:OUT_OF_CREDITS",
                $"journal:{HistoryEventKind.BranchFailed}",
                $"release:{RunId}:1"
            },
            _log);
        _cancellation.Verify(cancellation => cancellation.FailRunAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Running_out_of_credits_with_onFailure_FailRun_cancels_the_run_instead_of_failing_it()
    {
        var node = ActionNode(ErrorOutcome.FailRun);
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));
        var outOfCredits = new NodeExecutionResult.Fail("OUT_OF_CREDITS", "budget spent", false, null);

        await CreateHandler(branches).HandleAsync(outOfCredits, Context(node, Definition([node], []), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "failed",
                "cancel-run:OUT_OF_CREDITS",
                $"journal:{HistoryEventKind.BranchFailed}",
                $"release:{RunId}:1"
            },
            _log);
    }

    /// <summary>
    /// A failed cohort branch never reaches its Join, so it contributes "failed" itself; when that
    /// arrival meets the quorum, the work after the Join goes on in a fresh, counted branch.
    /// </summary>
    [Fact]
    public async Task A_cohort_branch_contributes_its_failure_and_spawns_the_join_continuation()
    {
        _joins
            .Setup(joins => joins.ContributeAsync(JoinToken, BranchId, "failed", It.IsAny<CancellationToken>()))
            .Callback(() => _log.Add("contribute"))
            .ReturnsAsync(new JoinContributionResult(true, 3, 2, 1, 3, JoinNodeId: JoinNode));
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode, $$"""{"{{JoinTokens.TokenKey}}":"{{JoinToken}}"}"""));
        var definition = Definition(
            [Node(FailingNode), Node(JoinNode), Node(AfterJoin)],
            [new Edge((FailingNode, "default"), (JoinNode, "in")), new Edge((JoinNode, "default"), (AfterJoin, "in"))]);

        await CreateHandler(branches).HandleAsync(Boom, Context(Node(FailingNode), definition, branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "failed",
                $"journal:{HistoryEventKind.BranchFailed}",
                "contribute",
                $"increment:{RunId}:1",
                $"create:{AfterJoin}",
                $"journal:{HistoryEventKind.JoinContinuationSpawned}",
                "dispatch:5000",
                $"release:{RunId}:1"
            },
            _log);

        var continuation = Assert.Single(branches.Created);
        Assert.Equal(BranchRefId, continuation.ParentBranchId);
        Assert.Equal(1, LocalState(continuation.LocalJson)["joinFailed"].GetInt32());
        Assert.Equal(JoinNode, _journal.Entries[1].NodeId);
        Assert.Equal(BranchExecutionReason.ForkChild, Assert.Single(_dispatcher.Requests).Reason);
    }

    /// <summary>
    /// In a cohort nested inside another, a continuation spawned by a failed last arrival must carry
    /// the enclosing cohort's token, or it reaches the outer Join with no token and the outer cohort
    /// never converges.
    /// </summary>
    [Fact]
    public async Task A_nested_cohort_continuation_rejoins_the_enclosing_cohort()
    {
        IReadOnlyDictionary<string, JsonElement> state = await SpawnContinuationFrom(
            $$"""{"{{JoinTokens.TokenKey}}":"{{JoinToken}}","{{JoinTokens.OuterKey}}":["{{OuterToken}}"],"item":"a"}""");

        Assert.Equal(OuterToken.ToString(), state[JoinTokens.TokenKey].GetString());
        Assert.False(state.ContainsKey(JoinTokens.OuterKey));
        Assert.Equal("a", state["item"].GetString());
        Assert.Equal(1, state["joinFailed"].GetInt32());
    }

    [Fact]
    public async Task A_doubly_nested_cohort_continuation_keeps_the_rest_of_the_stack()
    {
        IReadOnlyDictionary<string, JsonElement> state = await SpawnContinuationFrom(
            $$"""{"{{JoinTokens.TokenKey}}":"{{JoinToken}}","{{JoinTokens.OuterKey}}":["{{OutermostToken}}","{{OuterToken}}"]}""");

        Assert.Equal(OuterToken.ToString(), state[JoinTokens.TokenKey].GetString());
        Assert.Equal(
            new[] { OutermostToken.ToString() },
            state[JoinTokens.OuterKey].EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    /// <summary>
    /// At the outermost cohort the Join node leaves the state as it is, closed token included, so
    /// the failure path does the same and both continuations carry the same shape of state.
    /// </summary>
    [Fact]
    public async Task An_outermost_cohort_continuation_matches_what_the_join_node_would_carry()
    {
        IReadOnlyDictionary<string, JsonElement> state = await SpawnContinuationFrom(
            $$"""{"{{JoinTokens.TokenKey}}":"{{JoinToken}}"}""");

        Assert.Equal(JoinToken.ToString(), state[JoinTokens.TokenKey].GetString());
        Assert.False(state.ContainsKey(JoinTokens.OuterKey));
        Assert.Equal(3, state["joinContributed"].GetInt32());
        Assert.Equal(2, state["joinSucceeded"].GetInt32());
        Assert.Equal(1, state["joinFailed"].GetInt32());
    }

    [Fact]
    public async Task A_join_that_cannot_be_reached_does_not_mask_the_failure()
    {
        _joins
            .Setup(joins => joins.ContributeAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("join store down"));
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode, $$"""{"{{JoinTokens.TokenKey}}":"{{JoinToken}}"}"""));

        BranchResultOutcome outcome = await CreateHandler(branches).HandleAsync(Boom, Context(Node(FailingNode), Definition([Node(FailingNode)], []), branches.Current), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(new[] { "failed", $"journal:{HistoryEventKind.BranchFailed}", $"release:{RunId}:1" }, _log);
    }

    private async Task<IReadOnlyDictionary<string, JsonElement>> SpawnContinuationFrom(string failingBranchLocalJson)
    {
        _joins
            .Setup(joins => joins.ContributeAsync(JoinToken, BranchId, "failed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JoinContributionResult(true, 3, 2, 1, 3, JoinNodeId: JoinNode));
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode, failingBranchLocalJson));
        var definition = Definition(
            [Node(FailingNode), Node(JoinNode), Node(AfterJoin)],
            [new Edge((FailingNode, "default"), (JoinNode, "in")), new Edge((JoinNode, "default"), (AfterJoin, "in"))]);

        await CreateHandler(branches).HandleAsync(Boom, Context(Node(FailingNode), definition, branches.Current), CancellationToken.None);

        return LocalState(Assert.Single(branches.Created).LocalJson);
    }

    private FailResultHandler CreateHandler(RecordingBranchProvider branches)
    {
        return new FailResultHandler(
            branches,
            new RecordingRunCountersProvider(_log),
            _dispatcher,
            new SequentialIdGenerator(),
            new FixedClock(),
            _cancellation.Object,
            _compensation.Object,
            _joins.Object,
            _journal,
            new RecordingBranchTerminator(_log));
    }

    private static FailRunNode FailRun() => new()
    {
        NodeId = FailingNode,
        Name = "fail",
        Ports = [],
        Config = new FailRunConfig { Reason = "inventory issue" }
    };

    private static TestActionNode ActionNode(ErrorOutcome outcome) => new()
    {
        NodeId = FailingNode,
        Name = "action",
        Ports = [],
        OnFailure = new OnFailureConfig { Outcome = outcome }
    };
}
