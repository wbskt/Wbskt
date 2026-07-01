using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class BookmarkSchedulerOrphanGcTests
{
    [Fact]
    public async Task Scheduler_runs_orphan_gc_on_configured_interval()
    {
        // Arrange
        var provider = new RecordingBookmarkProvider();
        var scheduler = new BookmarkScheduler(
            new FixedClock(),
            new FixedHostIdentity(),
            new RecordingBranchProvider(),
            provider,
            new RecordingRunDispatcher(),
            NullLogger<BookmarkScheduler>.Instance,
            pollInterval: TimeSpan.FromHours(1),
            orphanGcInterval: TimeSpan.FromMilliseconds(20));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await scheduler.StartAsync(cts.Token);
        await provider.WaitForGcCallAsync(cts.Token);
        await scheduler.StopAsync(CancellationToken.None);

        // Assert
        Assert.True(provider.DeleteOrphansCallCount >= 1);
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class FixedHostIdentity : IHostIdentity
    {
        public string HostId => "host-1";
    }

    private sealed class RecordingBranchProvider : IBranchProvider
    {
        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingRunDispatcher : IRunDispatcher
    {
        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class RecordingBookmarkProvider : IBookmarkProvider
    {
        private readonly TaskCompletionSource _gcCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DeleteOrphansCallCount { get; private set; }

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Array.Empty<BookmarkRow>());
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct) => throw new NotSupportedException();
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


