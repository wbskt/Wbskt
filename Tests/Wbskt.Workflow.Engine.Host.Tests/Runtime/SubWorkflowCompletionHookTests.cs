using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class SubWorkflowCompletionHookTests
{
    [Fact]
    public async Task OnRunCompletedAsync_calls_hub_with_child_completed_channel()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.Queued, null, null, "ok"));
        var hook = new SubWorkflowCompletionHook(hub.Object, new FixedIdGenerator());
        Guid runRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        await hook.OnRunCompletedAsync(runRefId, "Succeeded", CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "child-completed"
                && e.MatchKeys.Contains($"child-completed:{runRefId}")
                && e.InboundEventId == $"child-completed:{runRefId}:11111111-1111-1111-1111-111111111111"
                && e.Payload["childRunRefId"].GetGuid() == runRefId
                && e.Payload["status"].GetString() == "Succeeded"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task OnRunCompletedAsync_uses_run_ref_id_in_correlation_key()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.Queued, null, null, "ok"));
        var hook = new SubWorkflowCompletionHook(hub.Object, new FixedIdGenerator());
        Guid runRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await hook.OnRunCompletedAsync(runRefId, "Failed", CancellationToken.None);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.MatchKeys.Contains("child-completed:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task RunFinalizer_calls_hook_after_finalization()
    {
        var bookmarkProvider = new RecordingBookmarkProvider();
        var hook = new RecordingCompletionHook(bookmarkProvider);
        var finalizer = new RunFinalizer(
            new RecordingRunProvider(),
            new StubBranchProvider(),
            new RecordingHistoryEventProvider(),
            new RecordingPendingTriggerEventDrainer(),
            new RecordingRunCompletedPublisher(),
            bookmarkProvider,
            hook,
            new FixedClock());

        await finalizer.FinalizeAsync(42, CancellationToken.None);

        Assert.True(hook.Called);
        Assert.True(hook.BookmarksDeletedBeforeCall);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), hook.RunRefId);
        Assert.Equal("Succeeded", hook.TerminalStatus);
    }

    private sealed class FixedIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.Parse("11111111-1111-1111-1111-111111111111");
    }

    private sealed class RecordingCompletionHook(RecordingBookmarkProvider bookmarkProvider) : ISubWorkflowCompletionHook
    {
        public bool Called { get; private set; }
        public bool BookmarksDeletedBeforeCall { get; private set; }
        public Guid RunRefId { get; private set; }
        public string? TerminalStatus { get; private set; }

        public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct)
        {
            Called = true;
            BookmarksDeletedBeforeCall = bookmarkProvider.DeletedRunIds.Contains(42);
            RunRefId = runRefId;
            TerminalStatus = terminalStatus;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRunProvider : IRunProvider
    {
        private readonly RunRow _run = new()
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
        };

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(_run);
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
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => Task.FromResult((true, _run with { Status = status, CompletedAt = completedAt }));
    }

    private sealed class StubBranchProvider : IBranchProvider
    {
        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>([new BranchRow { Id = 1001, RefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), RunId = 42, ParentBranchId = null, ForkCohortId = null, NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Status = "Completed", PendingTakePort = null, LocalJson = "{}", LastOutputJson = null, CompensationStackJson = null, CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc), RowVersion = [1] }]);
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingHistoryEventProvider : IHistoryEventProvider
    {
        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, DateTime? elevatedCutoffUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingPendingTriggerEventDrainer : IPendingTriggerEventDrainer
    {
        public Task DrainAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingRunCompletedPublisher : IRunCompletedPublisher
    {
        public Task PublishAsync(RunRow run, string terminalStatus, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingBookmarkProvider : IBookmarkProvider
    {
        public List<int> DeletedRunIds { get; } = [];
        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
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
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
        {
            DeletedRunIds.Add(runId);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }
}

