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
        RunRow child = EndedChild(7, """{"ok":false}""");
        var hook = new RecordingCompletionHook(expectedCalls: 1);
        BookmarkOrphanGc gc = CreateGc(TimeProvider.System, new EndedChildRunProvider([child]), hook);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await gc.StartAsync(cts.Token);
        await hook.Done.Task.WaitAsync(cts.Token);
        await gc.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal((child.RefId, RunStatus.Failed, """{"ok":false}"""), hook.Calls[0]);
    }

    [Fact]
    public async Task A_child_whose_completion_cannot_be_raised_does_not_stop_the_next()
    {
        // Arrange
        RunRow first = EndedChild(7, null);
        RunRow second = EndedChild(8, null);
        var hook = new RecordingCompletionHook(expectedCalls: 2, throwFor: first.RefId);
        BookmarkOrphanGc gc = CreateGc(TimeProvider.System, new EndedChildRunProvider([first, second]), hook);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await gc.StartAsync(cts.Token);
        await hook.Done.Task.WaitAsync(cts.Token);
        await gc.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(second.RefId, hook.Calls[1].RunRefId);
    }

    [Fact]
    public async Task Only_children_that_ended_a_stranded_threshold_ago_are_read()
    {
        // Arrange
        var clock = new FixedClock(new DateTime(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc));
        var runs = new EndedChildRunProvider([]);
        BookmarkOrphanGc gc = CreateGc(clock, runs, new RecordingCompletionHook(expectedCalls: 1), TimeSpan.FromMinutes(7));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await gc.StartAsync(cts.Token);
        DateTime cutoff = await runs.Cutoff.Task.WaitAsync(cts.Token);
        await gc.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new DateTime(2026, 5, 26, 12, 23, 0, DateTimeKind.Utc), cutoff);
    }

    private static BookmarkOrphanGc CreateGc(TimeProvider clock, IRunProvider runs, ISubWorkflowCompletionHook hook, TimeSpan? strandedThreshold = null)
    {
        var options = new WorkflowEngineOptions
        {
            BookmarkOrphanGcInterval = TimeSpan.FromMilliseconds(20),
            StrandedBranchThreshold = strandedThreshold ?? new WorkflowEngineOptions().StrandedBranchThreshold
        };

        return new BookmarkOrphanGc(
            clock,
            new FixedLeadershipGate(true),
            WorkerTestServices.Scopes(new RecordingBookmarkProvider(), new NoStrandedBranchProvider(), runs, hook),
            NullLogger<BookmarkOrphanGc>.Instance,
            WorkerTestServices.Options(options));
    }

    private static RunRow EndedChild(int id, string? outputJson) => new()
    {
        Id = id, RefId = Guid.NewGuid(), WorkflowDefinitionId = 1, WorkflowRefId = Guid.NewGuid(), WorkflowVersion = 1,
        TriggerNodeId = Guid.NewGuid(), CorrelationKey = null, Status = RunStatus.Failed,
        StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, CancellationRequestedAt = null,
        CancellationReason = null, CreditBudget = 0m, CreatedAt = DateTime.UtcNow, OutputJson = outputJson
    };

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class EndedChildRunProvider(IReadOnlyCollection<RunRow> children) : IRunProvider
    {
        public TaskCompletionSource<DateTime> Cutoff { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyCollection<RunRow>> GetEndedChildrenAwaitedAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            Cutoff.TrySetResult(cutoffUtc);
            return Task.FromResult(children);
        }

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

    private sealed class RecordingCompletionHook(int expectedCalls, Guid? throwFor = null) : ISubWorkflowCompletionHook
    {
        private readonly object _gate = new();

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(Guid RunRefId, string Status, string? OutputJson)> Calls { get; } = [];

        public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct) => OnRunCompletedAsync(runRefId, terminalStatus, null, ct);

        public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, string? outputJson, CancellationToken ct)
        {
            lock (_gate)
            {
                Calls.Add((runRefId, terminalStatus, outputJson));
                if (Calls.Count >= expectedCalls)
                {
                    Done.TrySetResult();
                }
            }

            if (runRefId == throwFor)
            {
                throw new InvalidOperationException("The hub is down.");
            }

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
