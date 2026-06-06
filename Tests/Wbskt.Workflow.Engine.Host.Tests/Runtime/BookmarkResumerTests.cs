using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class BookmarkResumerTests
{
    [Fact]
    public async Task MatchInbound_returns_idempotent_when_key_already_claimed()
    {
        // Arrange
        var bookmarkProvider = new RecordingBookmarkProvider();
        var idempotency = RecordingIdempotencyKeyProvider.AlreadyClaimed();
        var resumer = new BookmarkResumer(bookmarkProvider, idempotency, new RecordingBranchProvider(), new RecordingRunDispatcher());

        // Act
        BookmarkMatchResult result = await resumer.MatchInboundAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.False(result.Matched);
        Assert.Null(result.BookmarkId);
        Assert.True(result.Idempotent);
        Assert.Equal("mqtt:device-1:event-1", idempotency.LastKeyValue);
        Assert.Null(bookmarkProvider.LastMatchKey);
    }

    [Fact]
    public async Task MatchInbound_returns_unmatched_when_no_bookmark_exists()
    {
        // Arrange
        var bookmarkProvider = new RecordingBookmarkProvider();
        var idempotency = RecordingIdempotencyKeyProvider.NewClaim();
        var resumer = new BookmarkResumer(bookmarkProvider, idempotency, new RecordingBranchProvider(), new RecordingRunDispatcher());

        // Act
        BookmarkMatchResult result = await resumer.MatchInboundAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.False(result.Matched);
        Assert.Null(result.BookmarkId);
        Assert.False(result.Idempotent);
        Assert.Equal("device-1", bookmarkProvider.LastMatchKey);
    }

    [Fact]
    public async Task MatchInbound_deletes_bookmark_before_dispatching_branch()
    {
        // Arrange
        var bookmark = CreateBookmark(77, 42, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var operationLog = new List<string>();
        var bookmarkProvider = new RecordingBookmarkProvider(bookmark, operationLog);
        var idempotency = RecordingIdempotencyKeyProvider.NewClaim();
        var branchProvider = new RecordingBranchProvider((bookmark.BranchRefId, 1001));
        var dispatcher = new RecordingRunDispatcher(operationLog);
        var resumer = new BookmarkResumer(bookmarkProvider, idempotency, branchProvider, dispatcher);

        // Act
        BookmarkMatchResult result = await resumer.MatchInboundAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.True(result.Matched);
        Assert.Equal(77L, result.BookmarkId);
        Assert.False(result.Idempotent);
        Assert.Equal(["delete", "dispatch"], operationLog);
    }

    [Fact]
    public async Task MatchInbound_dispatches_branch_for_matching_bookmark()
    {
        // Arrange
        var bookmark = CreateBookmark(77, 42, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var bookmarkProvider = new RecordingBookmarkProvider(bookmark);
        var idempotency = RecordingIdempotencyKeyProvider.NewClaim();
        var branchProvider = new RecordingBranchProvider((bookmark.BranchRefId, 1001));
        var dispatcher = new RecordingRunDispatcher();
        var resumer = new BookmarkResumer(bookmarkProvider, idempotency, branchProvider, dispatcher);

        // Act
        await resumer.MatchInboundAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal([(42L, 1001L, BranchExecutionReason.BookmarkResumed)], dispatcher.Requests);
        Assert.Equal([bookmark.RefId], bookmarkProvider.DeletedRefIds);
    }

    [Fact]
    public async Task MatchInbound_persists_wake_payload_into_branch_local_state()
    {
        // Arrange
        var bookmark = CreateBookmark(77, 42, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var bookmarkProvider = new RecordingBookmarkProvider(bookmark);
        var branchProvider = new RecordingBranchProvider((bookmark.BranchRefId, 1001));
        var dispatcher = new RecordingRunDispatcher();
        var resumer = new BookmarkResumer(bookmarkProvider, RecordingIdempotencyKeyProvider.NewClaim(), branchProvider, dispatcher);
        var evt = new InboundEvent("signal", "device-1", "event-1", new Dictionary<string, JsonElement>
        {
            ["signalName"] = JsonSerializer.SerializeToElement("approve"),
            ["body"] = JsonSerializer.SerializeToElement(new { approvedBy = "ops" })
        }, new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));

        // Act
        await resumer.MatchInboundAsync(evt, CancellationToken.None);

        // Assert
        (long branchId, string localJson) = Assert.Single(branchProvider.PointerUpdates);
        Assert.Equal(1001L, branchId);
        using JsonDocument doc = JsonDocument.Parse(localJson);
        JsonElement wake = doc.RootElement.GetProperty("__wake");
        Assert.Equal("approve", wake.GetProperty("signalName").GetString());
        Assert.Equal("ops", wake.GetProperty("body").GetProperty("approvedBy").GetString());
    }

    [Fact]
    public async Task MatchInbound_with_empty_payload_does_not_touch_branch_local_state()
    {
        var bookmark = CreateBookmark(77, 42, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var bookmarkProvider = new RecordingBookmarkProvider(bookmark);
        var branchProvider = new RecordingBranchProvider((bookmark.BranchRefId, 1001));
        var resumer = new BookmarkResumer(bookmarkProvider, RecordingIdempotencyKeyProvider.NewClaim(), branchProvider, new RecordingRunDispatcher());

        await resumer.MatchInboundAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Empty(branchProvider.PointerUpdates);
    }

    [Fact]
    public async Task ResumeByBookmarkId_dispatches_branch()
    {
        // Arrange
        var bookmark = CreateBookmark(88, 42, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var bookmarkProvider = new RecordingBookmarkProvider(bookmark);
        var branchProvider = new RecordingBranchProvider((bookmark.BranchRefId, 2001));
        var dispatcher = new RecordingRunDispatcher();
        var resumer = new BookmarkResumer(bookmarkProvider, RecordingIdempotencyKeyProvider.NewClaim(), branchProvider, dispatcher);

        // Act
        await resumer.ResumeViaBookmarkAsync(88, new Dictionary<string, JsonElement>(), CancellationToken.None);

        // Assert
        Assert.Equal([bookmark.RefId], bookmarkProvider.DeletedRefIds);
        Assert.Equal([(42L, 2001L, BranchExecutionReason.BookmarkResumed)], dispatcher.Requests);
    }

    [Fact]
    public async Task ResumeByBookmarkId_is_idempotent_when_bookmark_already_deleted()
    {
        // Arrange
        var bookmarkProvider = new RecordingBookmarkProvider();
        var dispatcher = new RecordingRunDispatcher();
        var resumer = new BookmarkResumer(bookmarkProvider, RecordingIdempotencyKeyProvider.NewClaim(), new RecordingBranchProvider(), dispatcher);

        // Act
        await resumer.ResumeViaBookmarkAsync(999, new Dictionary<string, JsonElement>(), CancellationToken.None);

        // Assert
        Assert.Empty(bookmarkProvider.DeletedRefIds);
        Assert.Empty(dispatcher.Requests);
    }

    private static InboundEvent CreateInboundEvent()
    {
        return new InboundEvent(
            "mqtt",
            "device-1",
            "event-1",
            new Dictionary<string, JsonElement>(),
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
    }

    private static BookmarkRow CreateBookmark(int id, int runId, Guid branchRefId, Guid refId)
    {
        return new BookmarkRow
        {
            Id = id,
            RefId = refId,
            RunId = runId,
            BranchRefId = branchRefId,
            NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            WakeConditionKind = "Inbound",
            MatchKey = "device-1",
            WakeConditionJson = "{}",
            ExpiresAt = null,
            TtlPort = null,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed class RecordingBookmarkProvider(BookmarkRow? bookmark = null, List<string>? operationLog = null) : IBookmarkProvider
    {
        public string? LastMatchKey { get; private set; }
        public List<Guid> DeletedRefIds { get; } = [];

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow?> GetByIdAsync(long bookmarkId, CancellationToken ct) => Task.FromResult(bookmark is not null && bookmark.Id == bookmarkId ? bookmark : null);
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct)
        {
            LastMatchKey = matchKey;
            IReadOnlyCollection<BookmarkRow> matches = bookmark is null ? Array.Empty<BookmarkRow>() : [bookmark];
            return Task.FromResult(matches);
        }

        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct)
        {
            operationLog?.Add("delete");
            DeletedRefIds.Add(refId);
            return Task.CompletedTask;
        }

        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct) => Task.CompletedTask;
        public Task<long> CountAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => Task.FromResult(0);
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingIdempotencyKeyProvider : IIdempotencyKeyProvider
    {
        private readonly Func<string, int, Guid, Guid, int, IdempotencyKeyRow> _factory;

        private RecordingIdempotencyKeyProvider(Func<string, int, Guid, Guid, int, IdempotencyKeyRow> factory)
        {
            _factory = factory;
        }

        public string? LastKeyValue { get; private set; }

        public static RecordingIdempotencyKeyProvider NewClaim()
        {
            return new RecordingIdempotencyKeyProvider((key, runId, branchRefId, nodeId, attempt) => new IdempotencyKeyRow
            {
                Id = 1,
                KeyValue = key,
                RunId = runId,
                BranchRefId = branchRefId,
                NodeId = nodeId,
                Attempt = attempt,
                Status = "Pending",
                ResultJson = null,
                ErrorJson = null,
                CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                CompletedAt = null
            });
        }

        public static RecordingIdempotencyKeyProvider AlreadyClaimed()
        {
            return new RecordingIdempotencyKeyProvider((key, _, _, _, _) => new IdempotencyKeyRow
            {
                Id = 1,
                KeyValue = key,
                RunId = 0,
                BranchRefId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                NodeId = Guid.Empty,
                Attempt = 0,
                Status = "Pending",
                ResultJson = null,
                ErrorJson = null,
                CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                CompletedAt = null
            });
        }

        public Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct)
        {
            LastKeyValue = keyValue;
            return Task.FromResult(_factory(keyValue, runId, branchRefId, nodeId, attempt));
        }

        public Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingBranchProvider(params (Guid RefId, long Id)[] branches) : IBranchProvider
    {
        private readonly Dictionary<Guid, BranchRow> _branches = branches.ToDictionary(
            branch => branch.RefId,
            branch => new BranchRow
            {
                Id = (int)branch.Id,
                RefId = branch.RefId,
                RunId = 42,
                ParentBranchId = null,
                ForkCohortId = null,
                NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
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
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(_branches[refId]);
        public List<(long BranchId, string LocalJson)> PointerUpdates { get; } = [];
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct)
        {
            PointerUpdates.Add((branchId, localJson));
            return Task.FromResult(_branches.Values.First());
        }
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingRunDispatcher(List<string>? operationLog = null) : IRunDispatcher
    {
        public List<(long RunId, long BranchId, BranchExecutionReason Reason)> Requests { get; } = [];

        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
        {
            operationLog?.Add("dispatch");
            Requests.Add((request.RunId, request.BranchId, request.Reason));
            return ValueTask.CompletedTask;
        }
    }
}


