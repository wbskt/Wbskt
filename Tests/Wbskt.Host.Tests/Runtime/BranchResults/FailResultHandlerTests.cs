using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
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

    private static readonly NodeExecutionResult.Fail Boom = new("BOOM", "node blew up", false, null);

    private readonly List<string> _log = [];
    private readonly RecordingRunJournal _journal;
    private readonly RecordingRunDispatcher _dispatcher;
    private readonly Mock<IRunProvider> _runs = new();
    private readonly Mock<IRunCancellationService> _cancellation = new();
    private readonly Mock<ICompensationOrchestrator> _compensation = new();
    private readonly Mock<IJoinAggregatorProvider> _joins = new();

    public FailResultHandlerTests()
    {
        _journal = new RecordingRunJournal(_log);
        _dispatcher = new RecordingRunDispatcher(_log);
        _runs
            .Setup(runs => runs.TransitionStatusAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .Callback((long _, string from, string to, DateTime? _, string? _, CancellationToken _) => _log.Add($"run:{from}->{to}"))
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
    public async Task FailRun_fails_the_branch_then_the_run_then_cancels_the_rest()
    {
        var node = ActionNode(ErrorOutcome.FailRun);
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));

        await CreateHandler(branches).HandleAsync(Boom, Context(node, Definition([node], []), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "failed",
                $"run:{RunStatus.Running}->{RunStatus.Failing}",
                $"cancel-run:FailRun cascade triggered by node {FailingNode}",
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
    public async Task FailFast_fails_the_run_and_running_out_of_credits_cancels_it()
    {
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode));
        var outOfCredits = new NodeExecutionResult.Fail("OUT_OF_CREDITS", "budget spent", false, null);

        await CreateHandler(branches).HandleAsync(outOfCredits, Context(Node(FailingNode), Definition([Node(FailingNode)], [], failFast: true), branches.Current), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "failed",
                $"run:{RunStatus.Running}->{RunStatus.Failing}",
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
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode, $$"""{"{{FailResultHandler.JoinTokenKey}}":"{{JoinToken}}"}"""));
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

    [Fact]
    public async Task A_join_that_cannot_be_reached_does_not_mask_the_failure()
    {
        _joins
            .Setup(joins => joins.ContributeAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("join store down"));
        var branches = new RecordingBranchProvider(_log, Branch(FailingNode, $$"""{"{{FailResultHandler.JoinTokenKey}}":"{{JoinToken}}"}"""));

        BranchResultOutcome outcome = await CreateHandler(branches).HandleAsync(Boom, Context(Node(FailingNode), Definition([Node(FailingNode)], []), branches.Current), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(new[] { "failed", $"journal:{HistoryEventKind.BranchFailed}", $"release:{RunId}:1" }, _log);
    }

    private FailResultHandler CreateHandler(RecordingBranchProvider branches)
    {
        return new FailResultHandler(
            branches,
            _runs.Object,
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

    private static TestActionNode ActionNode(ErrorOutcome outcome) => new()
    {
        NodeId = FailingNode,
        Name = "action",
        Ports = [],
        OnFailure = new OnFailureConfig { Outcome = outcome }
    };
}
