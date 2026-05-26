using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class BookmarkCompanionTimerTests
{
    [Fact]
    public async Task WaitForBookmark_with_Ttl_inserts_two_bookmarks_primary_and_companion()
    {
        // Arrange
        Guid nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        WorkflowDefinition definition = CreateDefinition(nodeId);
        var bookmarkProvider = new RecordingBookmarkProvider();
        var loop = new BranchLoop(
            new RecordingBranchProvider(nodeId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1001),
            new StubRunProvider(),
            new StubRunCountersProvider(),
            bookmarkProvider,
            new RecordingHistoryEventProvider(),
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new ScriptedExecutor(new NodeExecutionResult.WaitForBookmark(
                new SignalWakeCondition("operator-ack", "corr-42") { Ttl = TimeSpan.FromMinutes(5) },
                new Dictionary<string, JsonElement>()))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal(2, bookmarkProvider.Bookmarks.Count);
        Assert.Contains(bookmarkProvider.Bookmarks, bookmark => bookmark.WakeConditionKind == "signal");
        Assert.Contains(bookmarkProvider.Bookmarks, bookmark => bookmark.WakeConditionKind == "timer" && bookmark.ExpiresAt == new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Inbound_match_wins_deletes_companion_timer()
    {
        // Arrange
        Guid branchRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var primary = CreateBookmark(77, Guid.Parse("11111111-1111-1111-1111-111111111111"), 42, branchRefId, "inbound", "mqtt:device-1", null);
        var companion = CreateBookmark(78, Guid.Parse("22222222-2222-2222-2222-222222222222"), 42, branchRefId, "timer", string.Empty, new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc));
        var bookmarkProvider = new RecordingBookmarkProvider(primary, companion);
        var resumer = new BookmarkResumer(
            bookmarkProvider,
            RecordingIdempotencyKeyProvider.NewClaim(),
            new RecordingBranchProvider(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), branchRefId, 1001),
            new RecordingRunDispatcher());

        // Act
        await resumer.MatchInboundAsync(new InboundEvent("mqtt", "device-1", "event-1", new Dictionary<string, JsonElement>(), new DateTime(2026, 5, 26, 12, 31, 0, DateTimeKind.Utc)), CancellationToken.None);

        // Assert
        Assert.Empty(bookmarkProvider.Bookmarks);
        Assert.Equal((42L, 1001L, 77L), bookmarkProvider.LastSiblingDeleteCall);
    }

    [Fact]
    public async Task Timer_wakes_first_deletes_primary_inbound_bookmark()
    {
        // Arrange
        Guid branchRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var primary = CreateBookmark(77, Guid.Parse("11111111-1111-1111-1111-111111111111"), 42, branchRefId, "inbound", "mqtt:device-1", null);
        var timer = CreateBookmark(78, Guid.Parse("22222222-2222-2222-2222-222222222222"), 42, branchRefId, "timer", string.Empty, new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc));
        var bookmarkProvider = new RecordingBookmarkProvider(primary, timer) { DueBookmarks = [timer] };
        var scheduler = new BookmarkScheduler(
            new FixedClock(),
            new FixedHostIdentity(),
            new RecordingBranchProvider(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), branchRefId, 1001),
            bookmarkProvider,
            new RecordingRunDispatcher(),
            NullLogger<BookmarkScheduler>.Instance);

        // Act
        await scheduler.ProcessDueBookmarksAsync(CancellationToken.None);

        // Assert
        Assert.Empty(bookmarkProvider.Bookmarks);
        Assert.Equal((42L, 1001L, 78L), bookmarkProvider.LastSiblingDeleteCall);
    }

    [Fact]
    public async Task Sibling_delete_excludes_the_winner_bookmark_id()
    {
        // Arrange
        Guid branchRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var winner = CreateBookmark(77, Guid.Parse("11111111-1111-1111-1111-111111111111"), 42, branchRefId, "inbound", "mqtt:device-1", null);
        var sibling = CreateBookmark(78, Guid.Parse("22222222-2222-2222-2222-222222222222"), 42, branchRefId, "timer", string.Empty, new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc));
        var bookmarkProvider = new RecordingBookmarkProvider(winner, sibling);

        // Act
        await bookmarkProvider.DeleteSiblingsAsync(42, 1001, 77, CancellationToken.None);

        // Assert
        Assert.Single(bookmarkProvider.Bookmarks);
        Assert.Equal(77, bookmarkProvider.Bookmarks.Single().Id);
    }

    private static WorkflowDefinition CreateDefinition(Guid nodeId)
    {
        return new WorkflowDefinition(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            1,
            9,
            "ttl-companion",
            null,
            true,
            [new TestNode(nodeId, "wait", "test")],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
    }

    private static BookmarkRow CreateBookmark(int id, Guid refId, int runId, Guid branchRefId, string kind, string matchKey, DateTime? expiresAt)
    {
        return new BookmarkRow
        {
            Id = id,
            RefId = refId,
            RunId = runId,
            BranchRefId = branchRefId,
            NodeId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            WakeConditionKind = kind,
            MatchKey = matchKey,
            WakeConditionJson = "{}",
            ExpiresAt = expiresAt,
            TtlPort = null,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed record TestNode(Guid NodeId, string Name, string KindValue) : BaseNode(NodeId, Name, Array.Empty<PortDefinition>())
    {
        public override string Kind => KindValue;
    }

    private sealed class ScriptedExecutor(NodeExecutionResult result) : INodeExecutor
    {
        public string Kind => "test";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class StubNodeExecutorRegistry(INodeExecutor executor) : INodeExecutorRegistry
    {
        public INodeExecutor For(string kind) => executor;
    }

    private sealed class StubWorkflowDefinitionCache(WorkflowDefinition definition) : IWorkflowDefinitionCache
    {
        public Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct) => Task.FromResult(definition);
        public void Invalidate(int workflowDefinitionId) { }
    }

    private sealed class RecordingBranchProvider(Guid initialNodeId, Guid branchRefId, int branchId) : IBranchProvider
    {
        private readonly Dictionary<Guid, BranchRow> _byRefId = new()
        {
            [branchRefId] = new BranchRow
            {
                Id = branchId,
                RefId = branchRefId,
                RunId = 42,
                ParentBranchId = null,
                ForkCohortId = null,
                NodeId = initialNodeId,
                Status = "Active",
                PendingTakePort = null,
                LocalJson = "{}",
                LastOutputJson = null,
                CompensationStackJson = null,
                CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                RowVersion = [1]
            }
        };

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(_byRefId.Values.Single(row => row.Id == branchId));
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(_byRefId[refId]);
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(_byRefId.Values.ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct)
        {
            BranchRow updated = _byRefId.Values.Single(row => row.Id == branchId) with { NodeId = currentNodeId, Status = status, LocalJson = localJson, LastOutputJson = lastOutputJson };
            _byRefId[updated.RefId] = updated;
            return Task.FromResult(updated);
        }

        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubRunProvider : IRunProvider
    {
        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct)
        {
            return Task.FromResult(new RunRow
            {
                Id = (int)runId,
                RefId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                WorkflowDefinitionId = 9,
                WorkflowRefId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                WorkflowVersion = 1,
                TriggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CorrelationKey = "corr-42",
                Status = "Running",
                StartedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                CompletedAt = null,
                CancellationRequestedAt = null,
                CancellationReason = null,
                CreditBudget = 100,
                CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
            });
        }

        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubRunCountersProvider : IRunCountersProvider
    {
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

        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => Task.FromResult(1);
        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => Task.FromResult(1);
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(0m);
    }

    private sealed class RecordingBookmarkProvider(params BookmarkRow[] bookmarks) : IBookmarkProvider
    {
        private readonly List<BookmarkRow> _bookmarks = [.. bookmarks];

        public IReadOnlyList<BookmarkRow> Bookmarks => _bookmarks;
        public IReadOnlyCollection<BookmarkRow> DueBookmarks { get; init; } = Array.Empty<BookmarkRow>();
        public (long RunId, long BranchId, long ExcludeBookmarkId)? LastSiblingDeleteCall { get; private set; }

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct)
        {
            _bookmarks.Add(row);
            return Task.FromResult(row);
        }

        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(_bookmarks.Single(row => row.RefId == refId));
        public Task<BookmarkRow?> GetByIdAsync(long bookmarkId, CancellationToken ct) => Task.FromResult(_bookmarks.SingleOrDefault(row => row.Id == bookmarkId));
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(_bookmarks.Where(row => row.MatchKey == matchKey).ToArray());
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(_bookmarks.Where(row => row.RunId == runId).ToArray());
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => Task.FromResult(DueBookmarks);
        public Task DeleteAsync(Guid refId, CancellationToken ct)
        {
            _bookmarks.RemoveAll(row => row.RefId == refId);
            return Task.CompletedTask;
        }

        public Task<int> DeleteOrphansAsync(CancellationToken ct) => Task.FromResult(0);
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct)
        {
            LastSiblingDeleteCall = (runId, branchId, excludeBookmarkId);
            _bookmarks.RemoveAll(row => row.RunId == runId && row.Id != excludeBookmarkId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHistoryEventProvider : IHistoryEventProvider
    {
        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingRunDispatcher : IRunDispatcher
    {
        public List<BranchExecutionRequest> Requests { get; } = [];
        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingIdempotencyKeyProvider : IIdempotencyKeyProvider
    {
        public static RecordingIdempotencyKeyProvider NewClaim() => new();

        public Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct)
        {
            return Task.FromResult(new IdempotencyKeyRow
            {
                Id = 1,
                KeyValue = keyValue,
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

        public Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct) => throw new NotSupportedException();
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
        private readonly Queue<Guid> _ids = new([
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("66666666-6666-6666-6666-666666666666")]);

        public Guid NewId() => _ids.Dequeue();
    }

    private sealed class FixedHostIdentity : IHostIdentity
    {
        public string HostId => "host-1";
    }
}



