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

/// <summary>
/// Phase 1.4 — bookmarks use a single-row model (design §3.5): a wait with a TTL carries
/// ExpiresAt/TtlPort on the same row as the primary wake condition, rather than a separate
/// companion timer row. Whichever side claims (deletes) the row first wins.
/// </summary>
public sealed class BookmarkCompanionTimerTests
{
    [Fact]
    public async Task WaitForBookmark_with_Ttl_inserts_a_single_row_carrying_ExpiresAt_and_TtlPort()
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
                new SignalWakeCondition("operator-ack", "corr-42") { Ttl = TimeSpan.FromMinutes(5), TtlPort = "timeout" },
                new Dictionary<string, JsonElement>()))),
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert: exactly one row, carrying both the primary wake condition and the TTL fields.
        BookmarkRow row = Assert.Single(bookmarkProvider.Bookmarks);
        Assert.Equal("signal", row.WakeConditionKind);
        Assert.Equal(new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc), row.ExpiresAt);
        Assert.Equal("timeout", row.TtlPort);
    }

    [Fact]
    public async Task Inbound_match_claims_and_removes_the_single_bookmark_row()
    {
        // Arrange
        Guid branchRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bookmark = CreateBookmark(77, Guid.Parse("11111111-1111-1111-1111-111111111111"), 42, branchRefId, "inbound", "client-1", expiresAt: new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc), ttlPort: "timeout");
        var bookmarkProvider = new RecordingBookmarkProvider(bookmark);
        var dispatcher = new RecordingRunDispatcher();
        var resumer = new BookmarkResumer(
            bookmarkProvider,
            RecordingIdempotencyKeyProvider.NewClaim(),
            new RecordingBranchProvider(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), branchRefId, 1001),
            dispatcher);

        // Act
        BookmarkMatchResult result = await resumer.MatchInboundAsync(new InboundEvent("mqtt", ["client-1"], "event-1", new Dictionary<string, JsonElement>(), new DateTime(2026, 5, 26, 12, 31, 0, DateTimeKind.Utc)), CancellationToken.None);

        // Assert
        Assert.True(result.Matched);
        Assert.Empty(bookmarkProvider.Bookmarks);
        Assert.Single(dispatcher.Requests);
    }

    [Fact]
    public async Task Ttl_expiry_claims_the_single_bookmark_row_via_scheduler()
    {
        // Arrange
        Guid branchRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bookmark = CreateBookmark(78, Guid.Parse("22222222-2222-2222-2222-222222222222"), 42, branchRefId, "inbound", "mqtt:client-1", expiresAt: new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc), ttlPort: "timeout");
        var bookmarkProvider = new RecordingBookmarkProvider(bookmark) { DueBookmarks = [bookmark] };
        var branchProvider = new RecordingBranchProvider(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), branchRefId, 1001);
        var scheduler = new BookmarkScheduler(
            new FixedClock(),
            new FixedHostIdentity(),
            branchProvider,
            bookmarkProvider,
            new RecordingRunDispatcher(),
            NullLogger<BookmarkScheduler>.Instance);

        // Act
        await scheduler.ProcessDueBookmarksAsync(CancellationToken.None);

        // Assert: the TTL claim removed the row (claim-by-delete) and took the TtlPort edge.
        Assert.Empty(bookmarkProvider.Bookmarks);
        BranchRow branch = await branchProvider.GetByRefIdAsync(branchRefId, CancellationToken.None);
        Assert.Equal("timeout", branch.PendingTakePort);
    }

    [Fact]
    public async Task Second_claim_attempt_on_an_already_claimed_bookmark_loses_the_race()
    {
        // Arrange: models the signal-vs-TTL race - whichever claims (deletes) first wins;
        // the second claimant on the same RefId must observe failure, not double-resume.
        Guid refId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var bookmark = CreateBookmark(77, refId, 42, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "signal", "signal:approve:corr-42", expiresAt: null, ttlPort: null);
        var bookmarkProvider = new RecordingBookmarkProvider(bookmark);

        // Act
        bool firstClaim = await bookmarkProvider.TryClaimAsync(refId, CancellationToken.None);
        bool secondClaim = await bookmarkProvider.TryClaimAsync(refId, CancellationToken.None);

        // Assert
        Assert.True(firstClaim);
        Assert.False(secondClaim);
    }

    private static WorkflowDefinition CreateDefinition(Guid nodeId)
    {
        return new WorkflowDefinition(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            1,
            9,
            "ttl-single-row",
            null,
            true,
            [new TestNode { NodeId = nodeId, Name = "wait", Ports = Array.Empty<PortDefinition>(), KindValue = "test" }],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
    }

    private static BookmarkRow CreateBookmark(int id, Guid refId, int runId, Guid branchRefId, string kind, string matchKey, DateTime? expiresAt, string? ttlPort)
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
            TtlPort = ttlPort,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed record TestNode : BaseNode { public required string KindValue { get; init; } public override string Kind => KindValue; }

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
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct)
        {
            _byRefId[row.RefId] = row;
            return Task.FromResult(row);
        }
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(_byRefId.Values.Single(row => row.Id == branchId));
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(_byRefId[refId]);
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(_byRefId.Values.ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
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
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
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
        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(0m);
        public Task<bool> TryChargeAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class RecordingBookmarkProvider(params BookmarkRow[] bookmarks) : IBookmarkProvider
    {
        private readonly List<BookmarkRow> _bookmarks = [.. bookmarks];

        public IReadOnlyList<BookmarkRow> Bookmarks => _bookmarks;
        public IReadOnlyCollection<BookmarkRow> DueBookmarks { get; init; } = Array.Empty<BookmarkRow>();

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct)
        {
            _bookmarks.Add(row);
            return Task.FromResult(row);
        }

        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(_bookmarks.Single(row => row.RefId == refId));
        public Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct) => Task.FromResult(_bookmarks.Single(row => row.Id == bookmarkId));
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(_bookmarks.Where(row => row.MatchKey == matchKey).ToArray());
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeysAsync(IReadOnlyCollection<string> matchKeys, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(_bookmarks.Where(row => matchKeys.Contains(row.MatchKey)).ToArray());
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(_bookmarks.Where(row => row.RunId == runId).ToArray());

        public Task<bool> TryClaimAsync(Guid refId, CancellationToken ct)
        {
            int removed = _bookmarks.RemoveAll(row => row.RefId == refId);
            return Task.FromResult(removed > 0);
        }

        public Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct)
        {
            var due = DueBookmarks.Take(batchSize).ToArray();
            foreach (var row in due)
            {
                _bookmarks.RemoveAll(b => b.RefId == row.RefId);
            }
            return Task.FromResult<IReadOnlyCollection<BookmarkRow>>(due);
        }

        public Task DeleteAsync(Guid refId, CancellationToken ct)
        {
            _bookmarks.RemoveAll(row => row.RefId == refId);
            return Task.CompletedTask;
        }

        public Task<long> CountAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => Task.FromResult(0);
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
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
        public Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct) => Task.FromResult<IdempotencyKeyRow>(null!);
        public Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> ReclaimFailedAsync(string keyValue, Guid newBranchRefId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
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
