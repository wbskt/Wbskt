using Microsoft.Extensions.Caching.Memory;
using Wbskt.EventBus.Abstractions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class RunCancellationServiceTests
{
    [Fact]
    public async Task RequestCancellation_transitions_status_and_logs_history()
    {
        var runProvider = new RecordingRunProvider("Running", transitionResult: true);
        var historyProvider = new RecordingHistoryEventProvider();
        var service = new RunCancellationService(runProvider, historyProvider, new MemoryCache(new MemoryCacheOptions()), new FixedClock(), new DummyServiceProvider());

        bool cancelled = await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.True(cancelled);
        Assert.Equal((42L, "Running", "Cancelling"), runProvider.TransitionRequest);
        Assert.Equal("RunCancellationRequested", Assert.Single(historyProvider.Events).EventKind);
        Assert.Equal("operator", runProvider.UpdatedRun.CancellationReason);
    }

    [Fact]
    public async Task RequestCancellation_returns_false_when_already_terminal()
    {
        var runProvider = new RecordingRunProvider("Succeeded", transitionResult: false);
        var historyProvider = new RecordingHistoryEventProvider();
        var service = new RunCancellationService(runProvider, historyProvider, new MemoryCache(new MemoryCacheOptions()), new FixedClock(), new DummyServiceProvider());

        bool cancelled = await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.False(cancelled);
        Assert.Empty(historyProvider.Events);
    }

    [Fact]
    public async Task RequestCancellation_also_cancels_a_run_that_is_already_Failing()
    {
        // A run whose first branch failed sits in 'Failing' while its siblings keep going. Refusing to
        // cancel that was silent and inexplicable from the outside.
        var runProvider = new RecordingRunProvider("Failing", transitionResult: true, onlyFrom: "Failing");
        var historyProvider = new RecordingHistoryEventProvider();
        var service = new RunCancellationService(runProvider, historyProvider, new MemoryCache(new MemoryCacheOptions()), new FixedClock(), new DummyServiceProvider());

        bool cancelled = await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.True(cancelled);
        Assert.Equal((42L, "Failing", "Cancelling"), runProvider.TransitionRequest);
        Assert.Single(historyProvider.Events);
    }

    [Fact]
    public async Task RequestCancellation_does_the_local_work_for_a_run_another_host_already_transitioned()
    {
        // The cross-host case: the management host moved the status and published the event, but it has
        // no finalizer and does not own this host's bookmarks or branches. Short-circuiting on "I did not
        // transition it" left a parked cancelled run holding live bookmarks until the 30-minute reaper.
        var runProvider = new RecordingRunProvider("Cancelling", transitionResult: false);
        var historyProvider = new RecordingHistoryEventProvider();
        var branchProvider = new CancellationTestBranchProvider();
        var bookmarkProvider = new CancellationTestBookmarkProvider();
        var service = new RunCancellationService(
            runProvider,
            historyProvider,
            new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(),
            new DummyServiceProvider(),
            branchProvider,
            bookmarkProvider);

        bool cancelled = await service.RequestCancellationAsync(42, "from-the-bus", CancellationToken.None);

        Assert.True(cancelled);
        Assert.Equal(42, branchProvider.CancelledRunId);
        Assert.Equal(42, bookmarkProvider.DeletedRunId);
        // No duplicate history entry: this call transitioned nothing, so it has nothing new to record.
        Assert.Empty(historyProvider.Events);
    }

    [Fact]
    public async Task MarkCancellationRequested_makes_the_local_view_flip_immediately()
    {
        // Cancellation is read through a short per-host cache. Without this, a host that learns of a
        // cancel from the event bus keeps answering with whatever it cached before it.
        var runProvider = new RecordingRunProvider("Running", transitionResult: false);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new RunCancellationService(runProvider, new RecordingHistoryEventProvider(), cache, new FixedClock(), new DummyServiceProvider());

        Assert.False(await service.IsCancellationRequestedAsync(42, CancellationToken.None));

        service.MarkCancellationRequested(42);

        Assert.True(await service.IsCancellationRequestedAsync(42, CancellationToken.None));
        Assert.True(service.GetToken(42).IsCancellationRequested);
    }

    [Fact]
    public void The_token_registry_is_shared_across_scopes()
    {
        // WF-37. The service is Scoped and the pump runs each branch in its own scope, so a per-instance
        // registry meant the branch loop watched one token source while every canceller cancelled a
        // different, unobserved one - cancellation only ever took effect at the between-nodes check.
        var registry = new RunCancellationTokenRegistry();
        RunCancellationService branchScope = CreateService(registry);
        RunCancellationService cancellerScope = CreateService(registry);

        CancellationToken observed = branchScope.GetToken(42);
        cancellerScope.CancelCts(42);

        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public void A_cancel_that_arrives_before_the_branch_asks_for_its_token_is_not_lost()
    {
        var registry = new RunCancellationTokenRegistry();
        RunCancellationService service = CreateService(registry);

        service.CancelCts(42);

        Assert.True(service.GetToken(42).IsCancellationRequested);
    }

    private static RunCancellationService CreateService(RunCancellationTokenRegistry registry)
    {
        return new RunCancellationService(
            new RecordingRunProvider("Running", transitionResult: true),
            new RecordingHistoryEventProvider(),
            new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(),
            new DummyServiceProvider(),
            tokenRegistry: registry);
    }

    [Fact]
    public async Task IsCancellationRequested_returns_true_for_cancelling()
    {
        var service = new RunCancellationService(new RecordingRunProvider("Cancelling", transitionResult: false), new RecordingHistoryEventProvider(), new MemoryCache(new MemoryCacheOptions()), new FixedClock(), new DummyServiceProvider());

        bool cancelled = await service.IsCancellationRequestedAsync(42, CancellationToken.None);

        Assert.True(cancelled);
    }

    [Fact]
    public async Task RequestCancellation_deletes_bookmarks_and_cancels_waiting_branches()
    {
        var runProvider = new RecordingRunProvider("Running", transitionResult: true);
        var historyProvider = new RecordingHistoryEventProvider();
        var branchProvider = new CancellationTestBranchProvider();
        var bookmarkProvider = new CancellationTestBookmarkProvider();
        var service = new RunCancellationService(
            runProvider,
            historyProvider,
            new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(),
            new DummyServiceProvider(),
            branchProvider,
            bookmarkProvider);

        await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.Equal(42, branchProvider.CancelledRunId);
        Assert.Equal(42, bookmarkProvider.DeletedRunId);
    }

    [Fact]
    public async Task RequestCancellation_cancels_a_run_that_is_already_failing()
    {
        // A run whose first branch failed sits in 'Failing' while its siblings keep going. Refusing to
        // cancel that left the operator with a run they could see running and could not stop.
        var runProvider = new RecordingRunProvider("Failing", transitionResult: true, onlyFrom: "Failing");
        var historyProvider = new RecordingHistoryEventProvider();
        var service = new RunCancellationService(runProvider, historyProvider, new MemoryCache(new MemoryCacheOptions()), new FixedClock(), new DummyServiceProvider());

        bool cancelled = await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.True(cancelled);
        Assert.Equal([(42L, "Running", "Cancelling"), (42L, "Failing", "Cancelling")], runProvider.TransitionRequests);
        Assert.Equal("RunCancellationRequested", Assert.Single(historyProvider.Events).EventKind);
    }

    [Fact]
    public async Task RequestCancellation_still_cleans_up_when_another_host_already_transitioned_the_run()
    {
        // The management host transitions the status and publishes; the engine host consumes and lands
        // here. It must not treat "already Cancelling" as "nothing to do" - it owns the bookmarks and
        // branches, and without this the run keeps live bookmarks the reaper deliberately skips.
        var runProvider = new RecordingRunProvider("Cancelling", transitionResult: false);
        var historyProvider = new RecordingHistoryEventProvider();
        var branchProvider = new CancellationTestBranchProvider();
        var bookmarkProvider = new CancellationTestBookmarkProvider();
        var eventBus = new RecordingEventBus();
        var service = new RunCancellationService(
            runProvider,
            historyProvider,
            new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(),
            new DummyServiceProvider(),
            branchProvider,
            bookmarkProvider,
            eventBus: eventBus);

        bool cancelled = await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.True(cancelled);
        Assert.Equal(42, bookmarkProvider.DeletedRunId);
        Assert.Equal(42, branchProvider.CancelledRunId);
        // No second history entry for a transition this call did not make, and no republish - the two
        // hosts consume each other's events, so echoing one back would never stop.
        Assert.Empty(historyProvider.Events);
        Assert.Equal(0, eventBus.PublishCount);
    }

    [Fact]
    public async Task RequestCancellation_publishes_once_when_it_makes_the_transition()
    {
        var runProvider = new RecordingRunProvider("Running", transitionResult: true);
        var eventBus = new RecordingEventBus();
        var service = new RunCancellationService(
            runProvider,
            new RecordingHistoryEventProvider(),
            new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(),
            new DummyServiceProvider(),
            eventBus: eventBus);

        await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.Equal(1, eventBus.PublishCount);
    }

    [Fact]
    public async Task MarkCancellationRequested_is_visible_immediately_without_reading_the_run()
    {
        // The cross-host path: this host never issued the cancel, so without an explicit mark its cached
        // answer would stay stale for the rest of the cache window. The run row still reads 'Running'
        // here precisely to prove the answer is not coming from the database.
        var service = new RunCancellationService(
            new RecordingRunProvider("Running", transitionResult: false),
            new RecordingHistoryEventProvider(),
            new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(),
            new DummyServiceProvider());

        Assert.False(await service.IsCancellationRequestedAsync(42, CancellationToken.None));

        service.MarkCancellationRequested(42);

        Assert.True(await service.IsCancellationRequestedAsync(42, CancellationToken.None));
        Assert.True(service.GetToken(42).IsCancellationRequested);
    }

    [Fact]
    public async Task RequestCancellation_finalizes_a_run_left_with_nothing_running()
    {
        // A parked run's only branch is 'Waiting', so once it is cancelled no branch loop remains to
        // carry the run to a terminal status - it would sit in 'Cancelling' until the reaper.
        var runProvider = new RecordingRunProvider("Running", transitionResult: true);
        var finalizer = new RecordingRunFinalizer();
        var service = new RunCancellationService(
            runProvider,
            new RecordingHistoryEventProvider(),
            new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(),
            new SingleServiceProvider(finalizer),
            new CancellationTestBranchProvider(),
            new CancellationTestBookmarkProvider(),
            new IdleRunCountersProvider());

        await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.Equal([42L], finalizer.FinalizedRuns);
    }

    [Fact]
    public async Task RequestCancellation_leaves_finalization_to_the_branch_loop_while_a_branch_is_running()
    {
        var runProvider = new RecordingRunProvider("Running", transitionResult: true);
        var finalizer = new RecordingRunFinalizer();
        var service = new RunCancellationService(
            runProvider,
            new RecordingHistoryEventProvider(),
            new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(),
            new SingleServiceProvider(finalizer),
            new CancellationTestBranchProvider(),
            new CancellationTestBookmarkProvider(),
            new IdleRunCountersProvider(remainingAfterDecrement: 1));

        await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.Empty(finalizer.FinalizedRuns);
    }

    [Fact]
    public async Task BranchLoop_short_circuits_to_cancelled_when_cancellation_requested()
    {
        var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var definition = new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "cancelled-loop",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "start", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var branchProvider = new RecordingBranchProvider(nodeId);
        var counters = new StubRunCountersProvider();
        var loop = new BranchLoop(
            branchProvider,
            new BranchLoopRunProvider(),
            counters,
            new RecordingBookmarkProvider(),
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new FailIfCalledExecutor()),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator(),
            null,
            new AlwaysCancelledRunCancellationService());

        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        Assert.Equal("Cancelled", branchProvider.CurrentBranch.Status);
        Assert.Equal([-1], counters.IncrementCalls);
    }

    private sealed class RecordingRunProvider(string status, bool transitionResult, string? onlyFrom = null) : IRunProvider
    {
        public List<(long RunId, string FromStatus, string ToStatus)> TransitionRequests { get; } = [];

        public (long RunId, string FromStatus, string ToStatus)? TransitionRequest =>
            TransitionRequests.Count == 0 ? null : TransitionRequests[^1];

        public RunRow UpdatedRun { get; private set; } = CreateRun(status);

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(UpdatedRun);
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();

        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct)
        {
            UpdatedRun = UpdatedRun with { Status = status, CompletedAt = completedAt, CancellationRequestedAt = cancellationRequestedAt, CancellationReason = cancellationReason };
            return Task.FromResult(UpdatedRun);
        }

        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct)
        {
            TransitionRequests.Add((runId, fromStatus, toStatus));
            bool transitioned = transitionResult && (onlyFrom is null || string.Equals(onlyFrom, fromStatus, StringComparison.Ordinal));
            if (transitioned)
            {
                UpdatedRun = UpdatedRun with
                {
                    Status = toStatus,
                    CancellationRequestedAt = cancellationRequestedAt ?? UpdatedRun.CancellationRequestedAt,
                    CancellationReason = cancellationReason ?? UpdatedRun.CancellationReason
                };
            }

            return Task.FromResult(transitioned);
        }

        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();

        private static RunRow CreateRun(string status)
        {
            return new RunRow
            {
                Id = 42,
                RefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                WorkflowDefinitionId = 9,
                WorkflowRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                WorkflowVersion = 1,
                TriggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CorrelationKey = "corr-42",
                Status = status,
                StartedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                CompletedAt = null,
                CancellationRequestedAt = null,
                CancellationReason = null,
                CreditBudget = 100m,
                CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
            };
        }
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

    private sealed class AlwaysCancelledRunCancellationService : IRunCancellationService
    {
        public Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FailIfCalledExecutor : INodeExecutor
    {
        public string Kind => "test";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            throw new Xunit.Sdk.XunitException("Executor should not have been called.");
        }
    }

    private sealed class BranchLoopRunProvider : IRunProvider
    {
        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(new RunRow
        {
            Id = 42,
            RefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowDefinitionId = 9,
            WorkflowRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            WorkflowVersion = 1,
            TriggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CorrelationKey = "corr-42",
            Status = "Running",
            StartedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 100m,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
        });
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed record TestNode : BaseNode { public required string KindValue { get; init; } public override string Kind => KindValue; }

    private sealed class RecordingBranchProvider : IBranchProvider
    {
        private readonly Dictionary<long, BranchRow> _rows = new();

        public RecordingBranchProvider(Guid nodeId)
        {
            _rows[1001] = new BranchRow
            {
                Id = 1001,
                RefId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                RunId = 42,
                ParentBranchId = null,
                ForkCohortId = null,
                NodeId = nodeId,
                Status = "Active",
                PendingTakePort = null,
                LocalJson = "{}",
                LastOutputJson = null,
                CompensationStackJson = null,
                CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                RowVersion = [1]
            };
        }

        public BranchRow CurrentBranch => _rows[1001];

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(_rows[branchId]);
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(_rows.Values.ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(_rows.Values.ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(_rows.Values.ToArray());

        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct)
        {
            BranchRow updated = _rows[branchId] with { NodeId = currentNodeId, Status = status, LocalJson = localJson, LastOutputJson = lastOutputJson };
            _rows[branchId] = updated;
            return Task.FromResult(updated);
        }

        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubRunCountersProvider : IRunCountersProvider
    {
        public List<int> IncrementCalls { get; } = [];

        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct)
        {
            return Task.FromResult(new RunCountersRow
            {
                RunId = runId,
                ActiveBranchCount = 1,
                CreditsConsumed = 0m,
                UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
            });
        }

        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            IncrementCalls.Add(delta);
            return Task.FromResult(0);
        }

        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryChargeAsync(int runId, decimal cost, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingBookmarkProvider : IBookmarkProvider
    {
        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryClaimAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubWorkflowDefinitionCache(WorkflowDefinition definition) : IWorkflowDefinitionCache
    {
        public Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct) => Task.FromResult(definition);
        public void Invalidate(int workflowDefinitionId) { }
    }

    private sealed class StubNodeExecutorRegistry(INodeExecutor executor) : INodeExecutorRegistry
    {
        public INodeExecutor For(string kind) => executor;
    }

    private sealed class RecordingRunDispatcher : IRunDispatcher
    {
        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct) => ValueTask.CompletedTask;
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

    private sealed class CancellationTestBranchProvider : IBranchProvider
    {
        public int CancelledRunId { get; private set; }
        public Task<int> CancelWaitingBranchesAsync(int runId, CancellationToken ct)
        {
            CancelledRunId = runId;
            return Task.FromResult(1);
        }

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotImplementedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotImplementedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotImplementedException();
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotImplementedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotImplementedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class CancellationTestBookmarkProvider : IBookmarkProvider
    {
        public int DeletedRunId { get; private set; }
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
        {
            DeletedRunId = runId;
            return Task.CompletedTask;
        }

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotImplementedException();
        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotImplementedException();
        public Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeysAsync(IReadOnlyCollection<string> matchKeys, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> TryClaimAsync(Guid refId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotImplementedException();
        public Task<long> CountAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class DummyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class SingleServiceProvider(IRunFinalizer finalizer) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IRunFinalizer) ? finalizer : null;
    }

    private sealed class RecordingRunFinalizer : IRunFinalizer
    {
        public List<long> FinalizedRuns { get; } = [];

        public Task FinalizeAsync(long runId, CancellationToken ct)
        {
            FinalizedRuns.Add(runId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public int PublishCount { get; private set; }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            PublishCount++;
            return Task.CompletedTask;
        }
    }

    // CancellationTestBranchProvider reports one waiting branch cancelled, so the decrement lands on
    // whatever this returns - zero meaning "nothing left running".
    private sealed class IdleRunCountersProvider(int remainingAfterDecrement = 0) : IRunCountersProvider
    {
        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult(new RunCountersRow
        {
            RunId = runId,
            ActiveBranchCount = remainingAfterDecrement,
            CreditsConsumed = 0m,
            UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
        });

        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => Task.FromResult(remainingAfterDecrement);
        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryChargeAsync(int runId, decimal cost, CancellationToken ct) => throw new NotSupportedException();
    }
}






