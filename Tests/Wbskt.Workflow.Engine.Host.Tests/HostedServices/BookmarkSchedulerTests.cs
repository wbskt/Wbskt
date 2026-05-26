using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class BookmarkSchedulerTests
{
    [Fact]
    public async Task Tick_leases_due_bookmarks_and_dispatches_each()
    {
        // Arrange
        var first = CreateBookmark(11, 101, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var second = CreateBookmark(12, 102, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var provider = new RecordingBookmarkProvider([first, second]);
        var branchProvider = new RecordingBranchProvider((first.BranchRefId, 101), (second.BranchRefId, 102));
        var dispatcher = new RecordingRunDispatcher();
        var scheduler = new BookmarkScheduler(new FixedClock(), new FixedHostIdentity(), branchProvider, provider, dispatcher, NullLogger<BookmarkScheduler>.Instance);

        // Act
        await scheduler.ProcessDueBookmarksAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.Equal((11L, 101L, BranchExecutionReason.BookmarkResumed), dispatcher.Requests[0]);
        Assert.Equal((12L, 102L, BranchExecutionReason.BookmarkResumed), dispatcher.Requests[1]);
        Assert.Equal(64, provider.LastBatchSize);
        Assert.Equal("host-1", provider.LastHostId);
        Assert.Equal(TimeSpan.FromMinutes(2), provider.LastLeaseDuration);
    }

    [Fact]
    public async Task Tick_deletes_bookmark_after_successful_dispatch()
    {
        // Arrange
        var bookmark = CreateBookmark(33, 201, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"), Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var provider = new RecordingBookmarkProvider([bookmark]);
        var branchProvider = new RecordingBranchProvider((bookmark.BranchRefId, 201));
        var dispatcher = new RecordingRunDispatcher();
        var scheduler = new BookmarkScheduler(new FixedClock(), new FixedHostIdentity(), branchProvider, provider, dispatcher, NullLogger<BookmarkScheduler>.Instance);

        // Act
        await scheduler.ProcessDueBookmarksAsync(CancellationToken.None);

        // Assert
        Assert.Equal([bookmark.RefId], provider.DeletedRefIds);
    }

    [Fact]
    public async Task Tick_does_not_delete_when_dispatch_throws()
    {
        // Arrange
        var bookmark = CreateBookmark(33, 201, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"), Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var provider = new RecordingBookmarkProvider([bookmark]);
        var branchProvider = new RecordingBranchProvider((bookmark.BranchRefId, 201));
        var dispatcher = new RecordingRunDispatcher { ExceptionToThrow = new InvalidOperationException("boom") };
        var scheduler = new BookmarkScheduler(new FixedClock(), new FixedHostIdentity(), branchProvider, provider, dispatcher, NullLogger<BookmarkScheduler>.Instance);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.ProcessDueBookmarksAsync(CancellationToken.None));

        // Assert
        Assert.Empty(provider.DeletedRefIds);
    }

    [Fact]
    public async Task Empty_lease_result_does_not_call_dispatcher()
    {
        // Arrange
        var provider = new RecordingBookmarkProvider([]);
        var dispatcher = new RecordingRunDispatcher();
        var scheduler = new BookmarkScheduler(new FixedClock(), new FixedHostIdentity(), new RecordingBranchProvider(), provider, dispatcher, NullLogger<BookmarkScheduler>.Instance);

        // Act
        await scheduler.ProcessDueBookmarksAsync(CancellationToken.None);

        // Assert
        Assert.Empty(dispatcher.Requests);
        Assert.Empty(provider.DeletedRefIds);
    }

    private static BookmarkRow CreateBookmark(int runId, int branchId, Guid branchRefId, Guid refId)
    {
        _ = branchId;

        return new BookmarkRow
        {
            Id = runId * 10,
            RefId = refId,
            RunId = runId,
            BranchRefId = branchRefId,
            NodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WakeConditionKind = "Timer",
            MatchKey = string.Empty,
            WakeConditionJson = "{}",
            ExpiresAt = new DateTime(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc),
            TtlPort = null,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class FixedHostIdentity : IHostIdentity
    {
        public string HostId => "host-1";
    }

    private sealed class RecordingRunDispatcher : IRunDispatcher
    {
        public List<(long RunId, long BranchId, BranchExecutionReason Reason)> Requests { get; } = [];

        public Exception? ExceptionToThrow { get; init; }

        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
        {
            Requests.Add((request.RunId, request.BranchId, request.Reason));
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingBranchProvider(params (Guid RefId, long Id)[] rows) : IBranchProvider
    {
        private readonly Dictionary<Guid, BranchRow> _rows = rows.ToDictionary(
            row => row.RefId,
            row => new BranchRow
            {
                Id = (int)row.Id,
                RefId = row.RefId,
                RunId = 1,
                ParentBranchId = null,
                ForkCohortId = null,
                NodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Status = "Waiting",
                PendingTakePort = null,
                LocalJson = "{}",
                LastOutputJson = null,
                CompensationStackJson = null,
                CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                RowVersion = [1]
            });

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(_rows[refId]);
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingBookmarkProvider(IReadOnlyCollection<BookmarkRow> dueBookmarks) : IBookmarkProvider
    {
        public DateTime? LastNowUtc { get; private set; }
        public int LastBatchSize { get; private set; }
        public string? LastHostId { get; private set; }
        public TimeSpan? LastLeaseDuration { get; private set; }
        public List<Guid> DeletedRefIds { get; } = [];

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct)
        {
            LastNowUtc = nowUtc;
            LastBatchSize = batchSize;
            LastHostId = hostId;
            LastLeaseDuration = leaseDuration;
            return Task.FromResult(dueBookmarks);
        }
        public Task DeleteAsync(Guid refId, CancellationToken ct)
        {
            DeletedRefIds.Add(refId);
            return Task.CompletedTask;
        }

        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
    }
}
