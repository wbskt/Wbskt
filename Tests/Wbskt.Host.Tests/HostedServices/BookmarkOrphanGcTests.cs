using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Host.Tests.HostedServices;

public sealed class BookmarkOrphanGcTests
{
    [Fact]
    public async Task Orphan_gc_runs_on_its_configured_interval()
    {
        // Arrange
        var provider = new RecordingBookmarkProvider();
        var gc = new BookmarkOrphanGc(
            TimeProvider.System,
            new FixedLeadershipGate(true),
            WorkerTestServices.Scopes(provider, new NoStrandedBranchProvider()),
            NullLogger<BookmarkOrphanGc>.Instance,
            WorkerTestServices.Options(new WorkflowEngineOptions { BookmarkOrphanGcInterval = TimeSpan.FromMilliseconds(20) }));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await gc.StartAsync(cts.Token);
        await provider.WaitForGcCallAsync(cts.Token);
        await gc.StopAsync(CancellationToken.None);

        // Assert
        Assert.True(provider.DeleteOrphansCallCount >= 1);
    }

    [Fact]
    public async Task A_parent_still_waiting_on_an_ended_child_is_woken()
    {
        // Arrange
        var child = new RunRow
        {
            Id = 7, RefId = Guid.NewGuid(), WorkflowDefinitionId = 1, WorkflowRefId = Guid.NewGuid(), WorkflowVersion = 1,
            TriggerNodeId = Guid.NewGuid(), CorrelationKey = null, Status = RunStatus.Failed,
            StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, CancellationRequestedAt = null,
            CancellationReason = null, CreditBudget = 0m, CreatedAt = DateTime.UtcNow, OutputJson = """{"ok":false}"""
        };
        var runs = new EndedChildRunProvider([child]);
        var hook = new RecordingCompletionHook();
        var gc = new BookmarkOrphanGc(
            TimeProvider.System,
            new FixedLeadershipGate(true),
            WorkerTestServices.Scopes(new RecordingBookmarkProvider(), new NoStrandedBranchProvider(), runs, hook),
            NullLogger<BookmarkOrphanGc>.Instance,
            WorkerTestServices.Options(new WorkflowEngineOptions { BookmarkOrphanGcInterval = TimeSpan.FromMilliseconds(20) }));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await gc.StartAsync(cts.Token);
        await hook.Called.Task.WaitAsync(cts.Token);
        await gc.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal((child.RefId, RunStatus.Failed, """{"ok":false}"""), hook.First);
    }

    private sealed class EndedChildRunProvider(IReadOnlyCollection<RunRow> children) : IRunProvider
    {
        public Task<IReadOnlyCollection<RunRow>> GetEndedChildrenAwaitedAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => Task.FromResult(children);
        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow?> FindRowByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingCompletionHook : ISubWorkflowCompletionHook
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public (Guid RunRefId, string Status, string? OutputJson)? First { get; private set; }

        public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct) => OnRunCompletedAsync(runRefId, terminalStatus, null, ct);

        public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, string? outputJson, CancellationToken ct)
        {
            First ??= (runRefId, terminalStatus, outputJson);
            Called.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class FixedLeadershipGate(bool isActive) : ILeadershipGate
    {
        public int Checks { get; private set; }

        public bool IsActive
        {
            get
            {
                Checks++;
                return isActive;
            }
        }
    }

    private sealed class NoStrandedBranchProvider : IBranchProvider
    {
        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingBookmarkProvider : IBookmarkProvider
    {
        private readonly TaskCompletionSource _gcCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DeleteOrphansCallCount { get; private set; }

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryClaimAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Array.Empty<BookmarkRow>());
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct)
        {
            DeleteOrphansCallCount++;
            _gcCalled.TrySetResult();
            return Task.FromResult(DeleteOrphansCallCount);
        }

        public Task WaitForGcCallAsync(CancellationToken ct) => _gcCalled.Task.WaitAsync(ct);
    }
}
