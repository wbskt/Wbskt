using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class RunFinalizerTests
{
    [Fact]
    public async Task FinalizeAsync_sets_succeeded_when_all_branches_completed()
    {
        var harness = new RunFinalizerHarness("Running", [CreateBranch("Completed")]);

        await harness.Finalizer.FinalizeAsync(42, CancellationToken.None);

        Assert.Equal("Succeeded", harness.RunProvider.TerminalStatus);
        Assert.Equal("RunFinalized", Assert.Single(harness.HistoryProvider.Events).EventKind);
        Assert.Equal(42, harness.Publisher.RunIds.Single());
    }

    [Fact]
    public async Task FinalizeAsync_sets_failed_when_all_branches_failed()
    {
        var harness = new RunFinalizerHarness("Running", [CreateBranch("Failed")]);

        await harness.Finalizer.FinalizeAsync(42, CancellationToken.None);

        Assert.Equal("Failed", harness.RunProvider.TerminalStatus);
    }

    [Fact]
    public async Task FinalizeAsync_sets_partially_failed_when_completed_and_failed_branches_exist()
    {
        var harness = new RunFinalizerHarness("Running", [CreateBranch("Completed"), CreateBranch("Failed")]);

        await harness.Finalizer.FinalizeAsync(42, CancellationToken.None);

        Assert.Equal("PartiallyFailed", harness.RunProvider.TerminalStatus);
    }

    [Fact]
    public async Task FinalizeAsync_sets_cancelled_when_run_is_cancelling()
    {
        var harness = new RunFinalizerHarness("Cancelling", [CreateBranch("Completed"), CreateBranch("Failed")]);

        await harness.Finalizer.FinalizeAsync(42, CancellationToken.None);

        Assert.Equal("Cancelled", harness.RunProvider.TerminalStatus);
    }

    [Fact]
    public async Task FinalizeAsync_drains_pending_events()
    {
        var harness = new RunFinalizerHarness("Running", [CreateBranch("Completed")]);

        await harness.Finalizer.FinalizeAsync(42, CancellationToken.None);

        Assert.Equal([(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.Parse("11111111-1111-1111-1111-111111111111"), "corr-42")], harness.Drainer.Requests);
    }

    [Fact]
    public async Task FinalizeAsync_deletes_remaining_bookmarks()
    {
        var harness = new RunFinalizerHarness("Running", [CreateBranch("Completed")]);

        await harness.Finalizer.FinalizeAsync(42, CancellationToken.None);

        Assert.Equal([42], harness.BookmarkProvider.DeletedRunIds);
    }

    private static BranchRow CreateBranch(string status)
    {
        return new BranchRow
        {
            Id = 1001,
            RefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            RunId = 42,
            ParentBranchId = null,
            ForkCohortId = null,
            NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Status = status,
            PendingTakePort = null,
            LocalJson = "{}",
            LastOutputJson = null,
            CompensationStackJson = null,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            RowVersion = [1]
        };
    }

    private sealed class RunFinalizerHarness
    {
        public RunFinalizerHarness(string runStatus, IReadOnlyCollection<BranchRow> branches)
        {
            RunProvider = new RecordingRunProvider(runStatus);
            CountersProvider = new StubRunCountersProvider();
            BranchProvider = new StubBranchProvider(branches);
            HistoryProvider = new RecordingHistoryEventProvider();
            Drainer = new RecordingPendingTriggerEventDrainer();
            Publisher = new RecordingRunCompletedPublisher();
            BookmarkProvider = new RecordingBookmarkProvider();
            CompletionHook = new NoOpCompletionHook();
            Finalizer = new RunFinalizer(RunProvider, CountersProvider, BranchProvider, HistoryProvider, Drainer, Publisher, BookmarkProvider, CompletionHook, new FixedClock());
        }

        public RecordingRunProvider RunProvider { get; }
        public StubRunCountersProvider CountersProvider { get; }
        public StubBranchProvider BranchProvider { get; }
        public RecordingHistoryEventProvider HistoryProvider { get; }
        public RecordingPendingTriggerEventDrainer Drainer { get; }
        public RecordingRunCompletedPublisher Publisher { get; }
        public RecordingBookmarkProvider BookmarkProvider { get; }
        public NoOpCompletionHook CompletionHook { get; }
        public RunFinalizer Finalizer { get; }
    }

    private sealed class RecordingRunProvider(string status) : IRunProvider
    {
        private RunRow _run = new()
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

        public string? TerminalStatus { get; private set; }

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(_run);
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct) => throw new NotSupportedException();

        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct)
        {
            TerminalStatus = status;
            _run = _run with { Status = status, CompletedAt = completedAt };
            return Task.FromResult(_run);
        }
    }

    private sealed class StubRunCountersProvider : IRunCountersProvider
    {
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

        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubBranchProvider(IReadOnlyCollection<BranchRow> branches) : IBranchProvider
    {
        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult(branches);
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
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

    private sealed class RecordingPendingTriggerEventDrainer : IPendingTriggerEventDrainer
    {
        public List<(Guid WorkflowRefId, Guid TriggerNodeId, string CorrelationKey)> Requests { get; } = [];

        public Task DrainAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
        {
            Requests.Add((workflowRefId, triggerNodeId, correlationKey));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRunCompletedPublisher : IRunCompletedPublisher
    {
        public List<long> RunIds { get; } = [];

        public Task PublishAsync(RunRow run, string terminalStatus, CancellationToken ct)
        {
            _ = terminalStatus;
            RunIds.Add(run.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBookmarkProvider : IBookmarkProvider
    {
        public List<int> DeletedRunIds { get; } = [];

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
        {
            DeletedRunIds.Add(runId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpCompletionHook : ISubWorkflowCompletionHook
    {
        public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }
}






