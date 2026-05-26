using Microsoft.Extensions.Caching.Memory;
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
        var service = new RunCancellationService(runProvider, historyProvider, new MemoryCache(new MemoryCacheOptions()), new FixedClock());

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
        var service = new RunCancellationService(runProvider, historyProvider, new MemoryCache(new MemoryCacheOptions()), new FixedClock());

        bool cancelled = await service.RequestCancellationAsync(42, "operator", CancellationToken.None);

        Assert.False(cancelled);
        Assert.Empty(historyProvider.Events);
    }

    [Fact]
    public async Task IsCancellationRequested_returns_true_for_cancelling()
    {
        var service = new RunCancellationService(new RecordingRunProvider("Cancelling", transitionResult: false), new RecordingHistoryEventProvider(), new MemoryCache(new MemoryCacheOptions()), new FixedClock());

        bool cancelled = await service.IsCancellationRequestedAsync(42, CancellationToken.None);

        Assert.True(cancelled);
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
            [new TestNode(nodeId, "start", "test")],
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

    private sealed class RecordingRunProvider(string status, bool transitionResult) : IRunProvider
    {
        public (long RunId, string FromStatus, string ToStatus)? TransitionRequest { get; private set; }

        public RunRow UpdatedRun { get; private set; } = CreateRun(status);

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(UpdatedRun);
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();

        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct)
        {
            UpdatedRun = UpdatedRun with { Status = status, CompletedAt = completedAt, CancellationRequestedAt = cancellationRequestedAt, CancellationReason = cancellationReason };
            return Task.FromResult(UpdatedRun);
        }

        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct)
        {
            TransitionRequest = (runId, fromStatus, toStatus);
            if (transitionResult)
            {
                UpdatedRun = UpdatedRun with { Status = toStatus };
            }

            return Task.FromResult(transitionResult);
        }

        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();

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

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
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
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed record TestNode(Guid NodeId, string Name, string KindValue) : BaseNode(NodeId, Name, Array.Empty<PortDefinition>())
    {
        public override string Kind => KindValue;
    }

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
    }

    private sealed class RecordingBookmarkProvider : IBookmarkProvider
    {
        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
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
}






