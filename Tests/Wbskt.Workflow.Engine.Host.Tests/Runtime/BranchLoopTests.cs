using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
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
    [Fact]
    public async Task RunAsync_executes_two_Continue_nodes_then_Terminal()
    {
        // Arrange
        var firstNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var thirdNodeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var definition = TestWorkflowDefinition.Create(
            new TestNode(firstNodeId, "start", "test"),
            new TestNode(secondNodeId, "middle", "test"),
            new TestNode(thirdNodeId, "end", "test"),
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
            [new TestNode(rootNodeId, "root", "test"), new TestNode(childNodeId, "child", "test")],
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
                new TestNode(rootNodeId, "root", "test"),
                new TestNode(childOneId, "one", "test"),
                new TestNode(childTwoId, "two", "test"),
                new TestNode(childThreeId, "three", "test")
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
            [new TestNode(rootNodeId, "root", "test"), new TestNode(childOneId, "one", "test"), new TestNode(childTwoId, "two", "test")],
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
            [new TestNode(nodeId, "wait", "test")],
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
            [new TestNode(nodeId, "fail", "test")],
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
            [new TestNode(nodeId, "noop", "test")],
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
                new SendCommandActionNode(
                    nodeId,
                    "fail",
                    [new PortDefinition("default", PortDirection.Output, "Default")],
                    new SendCommandConfig("device-1", "DoThing"),
                    new RetryPolicy(RetryStrategy.Constant, TimeSpan.Zero, null, null, 2, 0, []),
                    null)
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
            [new TestNode(startNodeId, "start", "test"), new TestNode(jumpedNodeId, "jumped", "test")],
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
                new SendCommandActionNode(
                    startNodeId,
                    "command",
                    [new PortDefinition("default", PortDirection.Output, "Default")],
                    new SendCommandConfig("device-1", "DoThing"),
                    null,
                    new OnFailureConfig(ErrorOutcome.ContinueAsSucceeded)),
                new TestNode(nextNodeId, "next", "test")
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
                new SendCommandActionNode(
                    startNodeId,
                    "command",
                    [new PortDefinition("default", PortDirection.Output, "Default")],
                    new SendCommandConfig("device-1", "DoThing"),
                    null,
                    new OnFailureConfig(ErrorOutcome.JumpToNode, TargetNodeId: jumpNodeId)),
                new TestNode(skippedNodeId, "skipped", "test"),
                new TestNode(jumpNodeId, "jump", "test")
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
            [new TestNode(nodeId, "fail", "test")],
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
            [new TestNode(nodeId, "fail", "test")],
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
            [new TestNode(nodeId, "fail", "test")],
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
            [new TestNode(nodeId, "fail", "test")],
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
            [new TestNode(nodeId, "crash", "test")],
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
            [new TestNode(nodeId, "terminal", "test")],
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
            [new TestNode(nodeId, "terminal", "test")],
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
            [new TestNode(nodeId, "terminal", "test")],
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
            [new TestNode(nodeId, "act", "test")],
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
            [new TestNode(nodeId, "act", "test")],
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

    private sealed record TestNode(Guid NodeId, string Name, string KindValue) : BaseNode(NodeId, Name, Array.Empty<PortDefinition>())
    {
        public override string Kind => KindValue;
    }

    private sealed class TestWorkflowDefinition
    {
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

    private sealed class ThrowingExecutor : INodeExecutor
    {
        public string Kind => "test";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            throw new InvalidOperationException("executor crashed");
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

        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct)
        {
            TransitionRequests.Add((runId, fromStatus, toStatus));
            if (!string.Equals(Status, fromStatus, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            Status = toStatus;
            return Task.FromResult(true);
        }

        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
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
        public Task<BookmarkRow?> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => throw new NotSupportedException();
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

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
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






