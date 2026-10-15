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
