using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class BranchLoopTests
{
    /// <summary>
    /// Regression: a node kind with no registered executor (e.g. a published "action:toast" node)
    /// must fail the branch cleanly instead of letting the exception escape RunAsync. If it escapes,
    /// the branch is left Active forever - the finalizer never runs, Run_GetStuck skips runs with
    /// Active branches so the reaper can't collect it, and RunRecoveryService re-dispatches it into
    /// the same crash on every restart.
    /// </summary>
    [Fact]
    public async Task RunAsync_fails_branch_cleanly_when_node_kind_has_no_executor()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = TestWorkflowDefinition.Create(
            new TestNode { NodeId = nodeId, Name = "toast", Ports = Array.Empty<PortDefinition>(), KindValue = "action:toast" });
        var branchProvider = new RecordingBranchProvider(nodeId);
        var countersProvider = new StubRunCountersProvider();
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            countersProvider,
            new RecordingBookmarkProvider(),
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            new UnsupportedKindNodeExecutorRegistry(),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act - must not throw
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert - the branch is drained, not left Active
        Assert.True(branchProvider.SetFailedCalled);
        Assert.Contains(-1, countersProvider.DecrementCalls); // the stub records -delta

        var failure = Assert.Single(historyProvider.Events, evt => evt.EventKind == "NodeFailed");
        Assert.Contains("NODE_KIND_NOT_SUPPORTED", failure.PayloadJson);
        Assert.Contains("action:toast", failure.PayloadJson);
    }

    /// <summary>
    /// Regression: a branch that fails inside a fan-out cohort never reaches its Join node, so the
    /// branch loop must contribute "failed" on its behalf. Without this a Mode=All cohort never
    /// reaches ExpectedCount, the aggregator is never claimed, and everything downstream of the Join
    /// is silently skipped until the reaper eventually collects the run.
    /// </summary>
    [Fact]
    public async Task RunAsync_contributes_failure_to_join_when_a_cohort_branch_fails()
    {
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var joinToken = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var definition = TestWorkflowDefinition.Create(
            new TestNode { NodeId = nodeId, Name = "failing", Ports = Array.Empty<PortDefinition>(), KindValue = "test" });

        var branchProvider = new RecordingBranchProvider(nodeId, $$"""{"__join_token":"{{joinToken}}"}""");
        var aggregator = new RecordingJoinAggregatorProvider(
            new JoinContributionResult(ShouldContinue: false, ContributedCount: 2, SucceededCount: 1, FailedCount: 1, ExpectedCount: 3));
        var registry = new StubNodeExecutorRegistry(
            new ScriptedExecutor(new NodeExecutionResult.Fail("BOOM", "node blew up", false, null)));

        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            registry,
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator(),
            joinAggregatorProvider: aggregator);

        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        Assert.True(branchProvider.SetFailedCalled);
        var contribution = Assert.Single(aggregator.Contributions);
        Assert.Equal(joinToken, contribution.JoinToken);
        Assert.Equal("failed", contribution.Outcome);
    }

    /// <summary>
    /// When the failed arrival is the one that satisfies the quorum, the Join would have continued -
    /// but the branch that would have carried it is dead. A fresh continuation branch is spawned at
    /// the node past the Join's "default" edge so the work after the Join still runs.
    /// </summary>
    [Fact]
    public async Task RunAsync_spawns_join_continuation_when_failed_arrival_meets_quorum()
    {
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var joinNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var afterJoinNodeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var joinToken = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var definition = TestWorkflowDefinition.Create(
            new TestNode { NodeId = nodeId, Name = "failing", Ports = Array.Empty<PortDefinition>(), KindValue = "test" },
            new TestNode { NodeId = joinNodeId, Name = "join", Ports = Array.Empty<PortDefinition>(), KindValue = "control:join" },
            new TestNode { NodeId = afterJoinNodeId, Name = "after", Ports = Array.Empty<PortDefinition>(), KindValue = "test" },
            new Edge((nodeId, "body"), (joinNodeId, "in")),
            new Edge((joinNodeId, "default"), (afterJoinNodeId, "in")));

        var branchProvider = new RecordingBranchProvider(nodeId, $$"""{"__join_token":"{{joinToken}}"}""");
        var aggregator = new RecordingJoinAggregatorProvider(
            new JoinContributionResult(true, 3, 2, 1, 3, JoinNodeId: joinNodeId));
        var counters = new StubRunCountersProvider();
        var dispatcher = new RecordingRunDispatcher();
        var registry = new StubNodeExecutorRegistry(
            new ScriptedExecutor(new NodeExecutionResult.Fail("BOOM", "node blew up", false, null)));

        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            registry,
            dispatcher,
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator(),
            joinAggregatorProvider: aggregator);

        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // A continuation branch was created at the node past the Join and dispatched...
        BranchRow continuation = Assert.Single(branchProvider.CreatedBranches);
        Assert.Equal(afterJoinNodeId, continuation.NodeId);
        Assert.Equal("Active", continuation.Status);
        Assert.Contains(dispatcher.Requests, request => request.BranchId == continuation.Id);

        // ...and it was counted BEFORE the failed branch's own decrement, so the run cannot finalize
        // out from under it.
        Assert.Contains(1, counters.IncrementCalls);
    }

    [Fact]
    public async Task RunAsync_records_a_resume_distinctly_from_a_start()
    {
        // A branch waking from a bookmark is not starting - often it parked hours ago. Logging both
        // as BranchStarted made the two indistinguishable in the trace.
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = TestWorkflowDefinition.Create(
            new TestNode { NodeId = nodeId, Name = "n", Ports = Array.Empty<PortDefinition>(), KindValue = "test" });
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = CreateLoop(definition, new RecordingBranchProvider(nodeId), historyProvider,
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed))));

        await loop.RunAsync(42, 1001, BranchExecutionReason.BookmarkResumed, CancellationToken.None);

        Assert.Contains(historyProvider.Events, evt => evt.EventKind == "BranchResumed");
        Assert.DoesNotContain(historyProvider.Events, evt => evt.EventKind == "BranchStarted");
    }

    [Fact]
    public async Task RunAsync_puts_the_node_duration_on_the_outcome_event()
    {
        // Deriving duration by subtracting the NodeStarted timestamp only works while both rows
        // survive retention, and silently includes retry backoff.
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = TestWorkflowDefinition.Create(
            new TestNode { NodeId = nodeId, Name = "n", Ports = Array.Empty<PortDefinition>(), KindValue = "test" });
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = CreateLoop(definition, new RecordingBranchProvider(nodeId), historyProvider,
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Continue("next", CreatePatch("step", 1)))));

        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        var completed = Assert.Single(historyProvider.Events, evt => evt.EventKind == "NodeCompleted");
        Assert.NotNull(completed.PayloadJson);
        Assert.True(JsonDocument.Parse(completed.PayloadJson!).RootElement.TryGetProperty("durationMs", out _));
    }

    [Fact]
    public async Task RunAsync_records_BranchFailed_alongside_NodeFailed()
    {
        // NodeFailed alone said which node broke, not that the branch ended nor how the failure was
        // handled - FailBranch, FailRun and Compensate look identical from NodeFailed.
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = TestWorkflowDefinition.Create(
            new TestNode { NodeId = nodeId, Name = "n", Ports = Array.Empty<PortDefinition>(), KindValue = "test" });
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = CreateLoop(definition, new RecordingBranchProvider(nodeId), historyProvider,
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Fail("BOOM", "blew up", false, null))));

        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        Assert.Contains(historyProvider.Events, evt => evt.EventKind == "NodeFailed");
        var branchFailed = Assert.Single(historyProvider.Events, evt => evt.EventKind == "BranchFailed");
        Assert.Equal("Warn", branchFailed.Severity);
        Assert.Contains("FailBranch", branchFailed.PayloadJson);
    }

    private static BranchLoop CreateLoop(
        WorkflowDefinition definition,
        RecordingBranchProvider branchProvider,
        RecordingHistoryEventProvider historyProvider,
        StubNodeExecutorRegistry registry)
    {
        return new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            registry,
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());
    }

    [Fact]
    public async Task RunAsync_executes_two_Continue_nodes_then_Terminal()
    {
        // Arrange
        var firstNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var thirdNodeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var definition = TestWorkflowDefinition.Create(
            new TestNode { NodeId = firstNodeId, Name = "start", Ports = Array.Empty<PortDefinition>(), KindValue = "test" },
            new TestNode { NodeId = secondNodeId, Name = "middle", Ports = Array.Empty<PortDefinition>(), KindValue = "test" },
            new TestNode { NodeId = thirdNodeId, Name = "end", Ports = Array.Empty<PortDefinition>(), KindValue = "test" },
            new Edge((firstNodeId, "next"), (secondNodeId, "in")),
            new Edge((secondNodeId, "next"), (thirdNodeId, "in")));
        var branchProvider = new RecordingBranchProvider(firstNodeId);
        var runProvider = new StubRunProvider();
        var registry = new StubNodeExecutorRegistry(
            new ScriptedExecutor(
                new NodeExecutionResult.Continue("next", CreatePatch("step", 1)),
                new NodeExecutionResult.Continue("next", CreatePatch("step", 2)),
                new NodeExecutionResult.Terminal(BranchTerminalReason.Completed)));
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = new BranchLoop(
            branchProvider,
            runProvider,
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            registry,
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal([secondNodeId, thirdNodeId], branchProvider.PointerUpdates.Select(update => update.CurrentNodeId));
        Assert.True(branchProvider.SetCompletedCalled);
        Assert.Equal(
            ["BranchStarted", "NodeStarted", "NodeCompleted", "NodeStarted", "NodeCompleted", "NodeStarted", "NodeCompleted", "BranchCompleted"],
            historyProvider.Events.Select(evt => evt.EventKind));
    }

    [Fact]
    public async Task RunAsync_inline_single_child_fork_recurses_without_dispatch()
    {
        // Arrange
        var rootNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var childNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "fork-inline",
            null,
            true,
            [new TestNode { NodeId = rootNodeId, Name = "root", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }, new TestNode { NodeId = childNodeId, Name = "child", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(rootNodeId);
        var dispatcher = new RecordingRunDispatcher();
        var counters = new StubRunCountersProvider();
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    new NodeExecutionResult.Fork([new ForkSpec(childNodeId.ToString(), CreatePatch("child", 1))], null, CreatePatch("fork", 1)),
                    new NodeExecutionResult.Terminal(BranchTerminalReason.Completed))),
            dispatcher,
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Empty(dispatcher.Requests);
        Assert.DoesNotContain(counters.IncrementCalls, value => value > 0);
        Assert.Contains(branchProvider.PointerUpdates, update => update.CurrentNodeId == childNodeId);
        Assert.True(branchProvider.SetCompletedCalled);
    }

    [Fact]
    public async Task RunAsync_multi_child_fork_dispatches_each_and_returns()
    {
        // Arrange
        var rootNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var childOneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var childTwoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var childThreeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "fork-multi",
            null,
            true,
            [
                new TestNode { NodeId = rootNodeId, Name = "root", Ports = Array.Empty<PortDefinition>(), KindValue = "test" },
                new TestNode { NodeId = childOneId, Name = "one", Ports = Array.Empty<PortDefinition>(), KindValue = "test" },
                new TestNode { NodeId = childTwoId, Name = "two", Ports = Array.Empty<PortDefinition>(), KindValue = "test" },
                new TestNode { NodeId = childThreeId, Name = "three", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }
            ],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(rootNodeId);
        var operationLog = new List<string>();
        var dispatcher = new RecordingRunDispatcher(operationLog);
        var counters = new StubRunCountersProvider(operationLog);
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    new NodeExecutionResult.Fork(
                    [
                        new ForkSpec(childOneId.ToString(), CreatePatch("child", 1)),
                        new ForkSpec(childTwoId.ToString(), CreatePatch("child", 2)),
                        new ForkSpec(childThreeId.ToString(), CreatePatch("child", 3))
                    ],
                    null,
                    CreatePatch("fork", 1)))),
            dispatcher,
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal(3, dispatcher.Requests.Count);
        Assert.Equal(3, branchProvider.CreatedBranches.Count);
        Assert.True(branchProvider.SetCompletedCalled);
        Assert.All(dispatcher.Requests, request => Assert.Equal(BranchExecutionReason.ForkChild, request.Reason));
    }

    [Fact]
    public async Task RunAsync_fork_increments_active_branches_counter_atomically()
    {
        // Arrange
        var rootNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var childOneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var childTwoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "fork-order",
            null,
            true,
            [new TestNode { NodeId = rootNodeId, Name = "root", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }, new TestNode { NodeId = childOneId, Name = "one", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }, new TestNode { NodeId = childTwoId, Name = "two", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var operationLog = new List<string>();
        var loop = new BranchLoop(
            new RecordingBranchProvider(rootNodeId),
            new StubRunProvider(),
            new StubRunCountersProvider(operationLog),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    new NodeExecutionResult.Fork(
                    [
                        new ForkSpec(childOneId.ToString(), CreatePatch("child", 1)),
                        new ForkSpec(childTwoId.ToString(), CreatePatch("child", 2))
                    ],
                    null,
                    CreatePatch("fork", 1)))),
            new RecordingRunDispatcher(operationLog),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal("increment:2", operationLog[0]);
        Assert.Equal(["increment:2", "dispatch", "dispatch"], operationLog.Take(3));
    }

    [Fact]
    public async Task RunAsync_WaitForBookmark_persists_bookmark_and_returns()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "wait",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "wait", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var bookmarkProvider = new RecordingBookmarkProvider();
        var branchProvider = new RecordingBranchProvider(nodeId);
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(),
            bookmarkProvider,
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    new NodeExecutionResult.WaitForBookmark(new SignalWakeCondition("operator-ack", "corr-42"), CreatePatch("waiting", 1)))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Single(branchProvider.PointerUpdates);
        Assert.Equal(nodeId, branchProvider.PointerUpdates[0].CurrentNodeId);
        Assert.Single(bookmarkProvider.CreatedBookmarks);
        Assert.Equal(nodeId, bookmarkProvider.CreatedBookmarks[0].NodeId);
        Assert.Contains(historyProvider.Events, evt => evt.EventKind == "BranchParked");
        Assert.False(branchProvider.SetCompletedCalled);
    }

    [Fact]
    public async Task Fail_non_retryable_marks_branch_failed_and_decrements_counter()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "fail",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "fail", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var counters = new StubRunCountersProvider();
        var branchProvider = new RecordingBranchProvider(nodeId);
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    new NodeExecutionResult.Fail("E_FAIL", "boom", false, null))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.True(branchProvider.SetFailedCalled);
        Assert.Equal([-1], counters.DecrementCalls);
        HistoryEventRow failedEvent = Assert.Single(historyProvider.Events, evt => evt.EventKind == "NodeFailed");
        Assert.NotNull(failedEvent.PayloadJson);
        using JsonDocument failedPayload = JsonDocument.Parse(failedEvent.PayloadJson!);
        Assert.Equal("E_FAIL", failedPayload.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("boom", failedPayload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Fail_out_of_credits_cascades_cancellation_to_siblings()
    {
        // Arrange: an OUT_OF_CREDITS fail on one branch must request run cancellation so
        // sibling branches stop burning an already-exhausted budget (2.2).
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "out-of-credits",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "fail", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var counters = new StubRunCountersProvider();
        var branchProvider = new RecordingBranchProvider(nodeId);
        var cancellationService = new RecordingRunCancellationService();
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    new NodeExecutionResult.Fail("OUT_OF_CREDITS", "Credit budget exhausted.", false, null))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator(),
            runFinalizer: null,
            runCancellationService: cancellationService);

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.True(branchProvider.SetFailedCalled);
        Assert.Equal([(42L, "OUT_OF_CREDITS")], cancellationService.Requests);
    }

    [Fact]
    public async Task NodeCompleted_history_event_records_port_payload()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "complete",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "noop", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var counters = new StubRunCountersProvider();
        var branchProvider = new RecordingBranchProvider(nodeId);
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>()))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        HistoryEventRow completedEvent = Assert.Single(historyProvider.Events, evt => evt.EventKind == "NodeCompleted");
        Assert.NotNull(completedEvent.PayloadJson);
        using JsonDocument completedPayload = JsonDocument.Parse(completedEvent.PayloadJson!);
        Assert.Equal("default", completedPayload.RootElement.GetProperty("port").GetString());
    }

    [Fact]
    public async Task Loop_invokes_retry_executor_on_each_step()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "fail-retry",
            null,
            true,
            [
                new SendClientMessageNode { NodeId = nodeId, Name = "fail", Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }], Config = new SendClientMessageConfig { ClientRef = "client-1", Type = "DoThing" }, Retry = new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 2, JitterPct = 0, RetryOn = [] } }
            ],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(nodeId);
        var executor = new ScriptedExecutor(
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(executor),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal(2, executor.CallCount);
        Assert.True(branchProvider.SetCompletedCalled);
        Assert.False(branchProvider.SetFailedCalled);
    }

    [Fact]
    public async Task Continue_with_node_id_outbound_port_jumps_directly_to_target_node()
    {
        // Arrange
        var startNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var jumpedNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "branch-loop-jump",
            null,
            true,
            [new TestNode { NodeId = startNodeId, Name = "start", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }, new TestNode { NodeId = jumpedNodeId, Name = "jumped", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(startNodeId);
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    NodeExecutionResult.JumpTo(jumpedNodeId, CreatePatch("jump", 1)),
                    new NodeExecutionResult.Terminal(BranchTerminalReason.Completed))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Contains(branchProvider.PointerUpdates, update => update.CurrentNodeId == jumpedNodeId);
        Assert.True(branchProvider.SetCompletedCalled);
    }

    [Fact]
    public async Task Loop_applies_on_failure_continue_as_succeeded()
    {
        // Arrange
        var startNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var nextNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "on-failure-continue",
            null,
            true,
            [
                new SendClientMessageNode { NodeId = startNodeId, Name = "command", Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }], Config = new SendClientMessageConfig { ClientRef = "client-1", Type = "DoThing" }, OnFailure = new OnFailureConfig { Outcome = ErrorOutcome.ContinueAsSucceeded } },
                new TestNode { NodeId = nextNodeId, Name = "next", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }
            ],
            [new Edge((startNodeId, "default"), (nextNodeId, "in"))],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(startNodeId);
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    new NodeExecutionResult.Fail("E_FAIL", "boom", false, null),
                    new NodeExecutionResult.Terminal(BranchTerminalReason.Completed))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Contains(branchProvider.PointerUpdates, update => update.CurrentNodeId == nextNodeId);
        Assert.True(branchProvider.SetCompletedCalled);
        Assert.False(branchProvider.SetFailedCalled);
    }

    [Fact]
    public async Task Loop_applies_on_failure_jump_to_node()
    {
        // Arrange
        var startNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var skippedNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var jumpNodeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "on-failure-jump",
            null,
            true,
            [
                new SendClientMessageNode { NodeId = startNodeId, Name = "command", Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }], Config = new SendClientMessageConfig { ClientRef = "client-1", Type = "DoThing" }, OnFailure = new OnFailureConfig { Outcome = ErrorOutcome.JumpToNode, TargetNodeId = jumpNodeId } },
                new TestNode { NodeId = skippedNodeId, Name = "skipped", Ports = Array.Empty<PortDefinition>(), KindValue = "test" },
                new TestNode { NodeId = jumpNodeId, Name = "jump", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }
            ],
            [new Edge((startNodeId, "default"), (skippedNodeId, "in"))],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(startNodeId);
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(
                new ScriptedExecutor(
                    new NodeExecutionResult.Fail("E_FAIL", "boom", false, null),
                    new NodeExecutionResult.Terminal(BranchTerminalReason.Completed))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Contains(branchProvider.PointerUpdates, update => update.CurrentNodeId == jumpNodeId);
        Assert.DoesNotContain(branchProvider.PointerUpdates, update => update.CurrentNodeId == skippedNodeId);
        Assert.True(branchProvider.SetCompletedCalled);
        Assert.False(branchProvider.SetFailedCalled);
    }

    [Fact]
    public async Task Branch_failure_invokes_compensation_when_definition_opts_in()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "compensation-opt-in",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "fail", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7,
            RunCompensationOnFailure: true);
        var compensationOrchestrator = new RecordingCompensationOrchestrator();
        var loop = new BranchLoop(
            new RecordingBranchProvider(nodeId),
            new StubRunProvider(),
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Fail("E_FAIL", "boom", false, null))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator(),
            compensationOrchestrator: compensationOrchestrator);

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal([(42L, 1001L)], compensationOrchestrator.Calls);
    }

    [Fact]
    public async Task Branch_fail_with_failFast_transitions_run_to_Failing()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "fail-fast",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "fail", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7,
            FailFast: true);
        var runProvider = new StubRunProvider();
        var loop = new BranchLoop(
            new RecordingBranchProvider(nodeId),
            runProvider,
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Fail("E_FAIL", "boom", false, null))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal([(42L, "Running", "Failing")], runProvider.TransitionRequests);
    }

    [Fact]
    public async Task Branch_fail_without_failFast_keeps_run_Running()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "no-fail-fast",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "fail", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var runProvider = new StubRunProvider();
        var loop = new BranchLoop(
            new RecordingBranchProvider(nodeId),
            runProvider,
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Fail("E_FAIL", "boom", false, null))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Empty(runProvider.TransitionRequests);
    }

    [Fact]
    public async Task Branch_fail_with_failFast_when_run_already_Cancelling_does_nothing()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "fail-fast-cancelling",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "fail", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7,
            FailFast: true);
        var runProvider = new StubRunProvider("Cancelling");
        var loop = new BranchLoop(
            new RecordingBranchProvider(nodeId),
            runProvider,
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Fail("E_FAIL", "boom", false, null))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal([(42L, "Running", "Failing")], runProvider.TransitionRequests);
        Assert.Equal("Cancelling", runProvider.Status);
    }

    [Fact]
    public async Task Loop_converts_thrown_exception_to_NonRetryable_Fail()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "executor-crash",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "crash", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(nodeId);
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ThrowingExecutor()),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.True(branchProvider.SetFailedCalled);
        Assert.Contains("EXECUTOR_CRASH", branchProvider.CurrentBranch.LastOutputJson);
    }

    [Fact]
    public async Task EngineFault_transitions_run_to_Faulted_and_drains_the_branch_slot()
    {
        // Arrange: an EngineFaultException is a terminal engine-level failure - the run goes
        // Faulted immediately, and this branch's slot must be drained (SetFailed + decrement)
        // directly since the finalizer is never invoked for the Faulted path (2.1).
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "engine-fault",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "fault", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(nodeId);
        var counters = new StubRunCountersProvider();
        var runProvider = new StubRunProvider();
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = new BranchLoop(
            branchProvider,
            runProvider,
            counters,
            new RecordingBookmarkProvider(),
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new FaultingExecutor()),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await Assert.ThrowsAsync<EngineFaultException>(() => loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None));

        // Assert
        Assert.Equal("Faulted", runProvider.Status);
        Assert.True(branchProvider.SetFailedCalled);
        Assert.Equal([-1], counters.DecrementCalls);
        Assert.Contains(historyProvider.Events, evt => evt.EventKind == "RunFaulted");
    }

    [Fact]
    public async Task RunAsync_leaves_branch_untouched_when_host_token_cancels_mid_node_execution()
    {
        // Arrange: host shutdown (not run cancellation) interrupts a provider call mid-node.
        // The branch must stay Active, untouched, so RunRecoveryService can re-dispatch it later.
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "host-shutdown-mid-node",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "act", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(nodeId);
        var historyProvider = new RecordingHistoryEventProvider();
        using var hostCts = new CancellationTokenSource();
        var counters = new HostShutdownRunCountersProvider(hostCts);
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, hostCts.Token);

        // Assert
        Assert.False(branchProvider.SetCompletedCalled);
        Assert.False(branchProvider.SetFailedCalled);
        Assert.Empty(branchProvider.PointerUpdates);
        Assert.Equal("Active", branchProvider.CurrentBranch.Status);
        Assert.Empty(counters.IncrementCalls);
        Assert.DoesNotContain(historyProvider.Events, evt => evt.EventKind is "NodeFailed" or "NodeCompleted" or "BranchCompleted");
    }

    [Fact]
    public async Task RunAsync_leaves_branch_untouched_when_host_token_cancels_after_node_returns()
    {
        // Arrange: the executor returns a normal (non-terminal) result, but host shutdown fired
        // while it was running. The branch must not be persisted as if the node had completed.
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "host-shutdown-after-node",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "act", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(nodeId);
        var counters = new StubRunCountersProvider();
        var historyProvider = new RecordingHistoryEventProvider();
        using var hostCts = new CancellationTokenSource();
        var executor = new CancelAfterReturnExecutor(hostCts, new NodeExecutionResult.Continue("next", CreatePatch("step", 1)));
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(executor),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, hostCts.Token);

        // Assert
        Assert.Empty(branchProvider.PointerUpdates);
        Assert.False(branchProvider.SetCompletedCalled);
        Assert.Equal("Active", branchProvider.CurrentBranch.Status);
    }

    [Fact]
    public async Task Terminal_calls_Branch_SetCompleted_and_decrements_counter()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "terminal",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "terminal", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var counters = new StubRunCountersProvider(incrementResults: [1]);
        var branchProvider = new RecordingBranchProvider(nodeId);
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.True(branchProvider.SetCompletedCalled);
        Assert.Equal([-1], counters.IncrementCalls);
    }

    [Fact]
    public async Task Terminal_when_post_decrement_count_is_zero_signals_run_finalizer()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "terminal-finalize",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "terminal", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var finalizer = new RecordingRunFinalizer();
        var loop = new BranchLoop(
            new RecordingBranchProvider(nodeId),
            new StubRunProvider(),
            new StubRunCountersProvider(incrementResults: [0]),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator(),
            finalizer);

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal([42L], finalizer.RunIds);
    }

    [Fact]
    public async Task Terminal_when_post_decrement_count_is_nonzero_does_not_call_finalizer()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "terminal-not-finalize",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "terminal", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var finalizer = new RecordingRunFinalizer();
        var loop = new BranchLoop(
            new RecordingBranchProvider(nodeId),
            new StubRunProvider(),
            new StubRunCountersProvider(incrementResults: [2]),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator(),
            finalizer);

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Empty(finalizer.RunIds);
    }

    [Fact]
    public async Task BuildBranchContext_hydrates_TriggerPayload_from_trigger_key_in_LocalJson()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "trigger-payload-hydration",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "act", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        const string localJson = """{"trigger":{"clientRefId":"cccccccc-cccc-cccc-cccc-cccccccccccc","clientId":7,"workspaceId":3}}""";
        var branchProvider = new RecordingBranchProvider(nodeId, localJson);
        var capturingExecutor = new CapturingExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(incrementResults: [1]),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(capturingExecutor),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.NotNull(capturingExecutor.LastContext);
        IReadOnlyDictionary<string, JsonElement> payload = capturingExecutor.LastContext.Branch.TriggerPayload;
        Assert.True(payload.ContainsKey("clientRefId"), "TriggerPayload should contain clientRefId");
        Assert.True(payload.ContainsKey("clientId"), "TriggerPayload should contain clientId");
        Assert.True(payload.ContainsKey("workspaceId"), "TriggerPayload should contain workspaceId");
        Assert.Equal("cccccccc-cccc-cccc-cccc-cccccccccccc", payload["clientRefId"].GetString());
        Assert.Equal(7, payload["clientId"].GetInt32());
        Assert.Equal(3, payload["workspaceId"].GetInt32());
    }

    [Fact]
    public async Task BuildBranchContext_TriggerPayload_is_empty_when_no_trigger_key_in_LocalJson()
    {
        // Arrange
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "no-trigger-key",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "act", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(nodeId, "{}");
        var capturingExecutor = new CapturingExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var loop = new BranchLoop(
            branchProvider,
            new StubRunProvider(),
            new StubRunCountersProvider(incrementResults: [1]),
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(capturingExecutor),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.NotNull(capturingExecutor.LastContext);
        Assert.Empty(capturingExecutor.LastContext.Branch.TriggerPayload);
    }

    private sealed class CapturingExecutor(NodeExecutionResult result) : INodeExecutor
    {
        public string Kind => "test";
        public NodeContext? LastContext { get; private set; }

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            LastContext = ctx;
            return Task.FromResult(result);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> CreatePatch(string name, int value)
    {
        return new Dictionary<string, JsonElement>
        {
            [name] = JsonDocument.Parse(value.ToString()).RootElement.Clone()
        };
    }

    private sealed record TestNode : BaseNode { public required string KindValue { get; init; } public override string Kind => KindValue; }

    private sealed class TestWorkflowDefinition
    {
        public static WorkflowDefinition Create(TestNode only)
        {
            return new WorkflowDefinition(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                1,
                9,
                "branch-loop",
                null,
                true,
                [only],
                [],
                [],
                new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                7);
        }

        public static WorkflowDefinition Create(TestNode first, TestNode second, TestNode third, Edge firstEdge, Edge secondEdge)
        {
            return new WorkflowDefinition(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                1,
                9,
                "branch-loop",
                null,
                true,
                [first, second, third],
                [firstEdge, secondEdge],
                [],
                new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                7);
        }
    }

    private sealed class ScriptedExecutor(params NodeExecutionResult[] results) : INodeExecutor
    {
        private readonly Queue<NodeExecutionResult> _results = new(results);

        public int CallCount { get; private set; }

        public string Kind => "test";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class StubNodeExecutorRegistry(INodeExecutor executor) : INodeExecutorRegistry
    {
        public INodeExecutor For(string kind) => executor;
    }

    /// <summary>Mimics the real registry's behaviour for a node kind that has no registered executor.</summary>
    private sealed class UnsupportedKindNodeExecutorRegistry : INodeExecutorRegistry
    {
        public INodeExecutor For(string kind) => throw new NodeKindNotSupportedException(kind);
    }

    private sealed class RecordingRunCancellationService : IRunCancellationService
    {
        public List<(long RunId, string Reason)> Requests { get; } = [];

        public Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
        {
            Requests.Add((runId, reason));
            return Task.FromResult(true);
        }

        public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class ThrowingExecutor : INodeExecutor
    {
        public string Kind => "test";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            throw new InvalidOperationException("executor crashed");
        }
    }

    private sealed class FaultingExecutor : INodeExecutor
    {
        public string Kind => "test";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            throw new EngineFaultException("engine fault");
        }
    }

    /// <summary>
    /// Simulates a host shutdown interrupting a provider call mid-node: RetryExecutor's credit
    /// charge (the only provider call between the initial branch/run fetch and the executor
    /// running) cancels the host token and throws, as a real ADO.NET call would when its command
    /// is cancelled.
    /// </summary>
    private sealed class HostShutdownRunCountersProvider(CancellationTokenSource hostCts) : IRunCountersProvider
    {
        public List<int> IncrementCalls { get; } = [];

        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            IncrementCalls.Add(delta);
            return Task.FromResult(0);
        }
        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TryChargeAsync(int runId, decimal cost, CancellationToken ct)
        {
            hostCts.Cancel();
            throw new OperationCanceledException("host shutdown");
        }
    }

    /// <summary>Returns a normal result but cancels the host token first, simulating shutdown racing with node completion.</summary>
    private sealed class CancelAfterReturnExecutor(CancellationTokenSource hostCts, NodeExecutionResult result) : INodeExecutor
    {
        public string Kind => "test";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            hostCts.Cancel();
            return Task.FromResult(result);
        }
    }

    private sealed class StubWorkflowDefinitionCache(WorkflowDefinition definition) : IWorkflowDefinitionCache
    {
        public Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct) => Task.FromResult(definition);

        public void Invalidate(int workflowDefinitionId)
        {
        }
    }

    private sealed class RecordingBranchProvider : IBranchProvider
    {
        private readonly Dictionary<long, BranchRow> _rows = new();
        private long _nextBranchId = 2000;

        public RecordingBranchProvider(Guid initialNodeId, string? initialLocalJson = null)
        {
            _rows[1001] = new BranchRow
            {
                Id = 1001,
                RefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                RunId = 42,
                ParentBranchId = null,
                ForkCohortId = null,
                NodeId = initialNodeId,
                Status = "Active",
                PendingTakePort = null,
                LocalJson = initialLocalJson ?? "{}",
                LastOutputJson = null,
                CompensationStackJson = null,
                CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                RowVersion = [1]
            };
        }

        private RecordingBranchProvider()
        {
        }

        public List<(long BranchId, Guid CurrentNodeId)> PointerUpdates { get; } = [];

        public List<BranchRow> CreatedBranches { get; } = [];

        public bool SetCompletedCalled { get; private set; }

        public bool SetFailedCalled { get; private set; }

        public BranchRow CurrentBranch => _rows[1001];

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
        {
            var created = row with { Id = (int)_nextBranchId++ };
            _rows[created.Id] = created;
            CreatedBranches.Add(created);
            return Task.FromResult(created);
        }

        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct)
        {
            _rows[row.Id] = row;
            return Task.FromResult(row);
        }

        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct)
        {
            return Task.FromResult(_rows[branchId]);
        }

        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct)
        {
            return Task.FromResult(_rows.Values.Single(row => row.RefId == refId));
        }

        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyCollection<BranchRow>>(_rows.Values.ToArray());
        }

        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyCollection<BranchRow>>(_rows.Values.Where(row => row.Status == "Active").ToArray());
        }

        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyCollection<BranchRow>>(_rows.Values.Where(row => row.Status == "Active").ToArray());
        }

        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct)
        {
            PointerUpdates.Add((branchId, currentNodeId));
            BranchRow updated = _rows[branchId] with { NodeId = currentNodeId, Status = status, LocalJson = localJson, LastOutputJson = lastOutputJson };
            _rows[branchId] = updated;
            return Task.FromResult(updated);
        }

        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct)
        {
            SetCompletedCalled = true;
            BranchRow updated = _rows[branchId] with { Status = "Completed" };
            _rows[branchId] = updated;
            return Task.FromResult(updated);
        }

        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct)
        {
            SetFailedCalled = true;
            BranchRow updated = _rows[branchId] with { Status = "Failed", LastOutputJson = lastOutputJson };
            _rows[branchId] = updated;
            return Task.FromResult(updated);
        }
    }

    private sealed class StubRunProvider(string initialStatus = "Running") : IRunProvider
    {
        public string Status { get; private set; } = initialStatus;

        public List<(long RunId, string FromStatus, string ToStatus)> TransitionRequests { get; } = [];

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();

        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();

        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct)
        {
            return Task.FromResult(new RunRow
            {
                Id = 42,
                RefId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                WorkflowDefinitionId = 9,
                WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                WorkflowVersion = 1,
                TriggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CorrelationKey = "corr-42",
                Status = Status,
                StartedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                CompletedAt = null,
                CancellationRequestedAt = null,
                CancellationReason = null,
                CreditBudget = 100,
                CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
            });
        }

        public Task<RunRow?> FindRowByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct)
        {
            TransitionRequests.Add((runId, fromStatus, toStatus));
            if (!string.Equals(Status, fromStatus, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            Status = toStatus;
            return Task.FromResult(true);
        }

        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubRunCountersProvider(List<string>? operationLog = null, IReadOnlyCollection<int>? incrementResults = null) : IRunCountersProvider
    {
        private readonly Queue<int> _incrementResults = new(incrementResults ?? [0]);

        public List<int> IncrementCalls { get; } = [];

        public List<int> DecrementCalls { get; } = [];

        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct)
        {
            return Task.FromResult(new RunCountersRow
            {
                RunId = runId,
                ActiveBranchCount = 0,
                CreditsConsumed = 0m,
                UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
            });
        }

        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            IncrementCalls.Add(delta);
            operationLog?.Add($"increment:{delta}");
            return Task.FromResult(_incrementResults.Count > 0 ? _incrementResults.Dequeue() : 0);
        }

        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            DecrementCalls.Add(-delta);
            operationLog?.Add($"decrement:{delta}");
            return Task.FromResult(0);
        }

        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(0m);
        public Task<bool> TryChargeAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class RecordingBookmarkProvider : IBookmarkProvider
    {
        public List<BookmarkRow> CreatedBookmarks { get; } = [];

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct)
        {
            CreatedBookmarks.Add(row);
            return Task.FromResult(row);
        }

        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryClaimAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingHistoryEventProvider : IHistoryEventProvider
    {
        public List<HistoryEventRow> Events { get; } = [];

        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, DateTime? elevatedCutoffUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingJoinAggregatorProvider(JoinContributionResult result) : IJoinAggregatorProvider
    {
        public List<(Guid JoinToken, string Outcome)> Contributions { get; } = [];

        public List<int> DeletedRunIds { get; } = [];

        public Task InitializeAsync(Guid joinToken, int runId, int expectedCount, string mode, int quorumCount, Guid? joinNodeId, CancellationToken ct)
            => Task.CompletedTask;

        public Task<JoinContributionResult> ContributeAsync(Guid joinToken, long branchId, string outcome, CancellationToken ct)
        {
            Contributions.Add((joinToken, outcome));
            return Task.FromResult(result);
        }

        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
        {
            DeletedRunIds.Add(runId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRunDispatcher(List<string>? operationLog = null) : IRunDispatcher
    {
        public List<BranchExecutionRequest> Requests { get; } = [];

        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
        {
            operationLog?.Add("dispatch");
            Requests.Add(request);
            return ValueTask.CompletedTask;
        }
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

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    }

    private sealed class RecordingRunFinalizer : IRunFinalizer
    {
        public List<long> RunIds { get; } = [];

        public Task FinalizeAsync(long runId, CancellationToken ct)
        {
            RunIds.Add(runId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCompensationOrchestrator : ICompensationOrchestrator
    {
        public List<(long RunId, long BranchId)> Calls { get; } = [];

        public Task RunAsync(long runId, long branchId, CancellationToken ct)
        {
            Calls.Add((runId, branchId));
            return Task.CompletedTask;
        }
    }
}






