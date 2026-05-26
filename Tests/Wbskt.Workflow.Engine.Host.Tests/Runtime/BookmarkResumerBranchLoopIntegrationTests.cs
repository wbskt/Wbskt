using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class BookmarkResumerBranchLoopIntegrationTests
{
    [Fact]
    public async Task WaitForBookmark_then_inbound_resume_completes_branch()
    {
        // Arrange
        Guid nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        WorkflowDefinition definition = new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "bookmark-e2e",
            null,
            true,
            [new TestNode(nodeId, "wait", "test")],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var providers = new InMemoryProviders(definition, nodeId);
        var dispatcher = new LoopBackDispatcher();
        var loop = new BranchLoop(
            providers,
            providers,
            providers,
            providers,
            providers,
            new StubWorkflowDefinitionCache(definition),
            new StubNodeExecutorRegistry(new WaitThenCompleteExecutor()),
            dispatcher,
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());
        dispatcher.Handler = request => loop.RunAsync(request.RunId, request.BranchId, request.Reason, CancellationToken.None);
        var resumer = new BookmarkResumer(providers, providers, providers, dispatcher);

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);
        BookmarkMatchResult result = await resumer.MatchInboundAsync(
            new InboundEvent("mqtt", "device-1", "event-1", new Dictionary<string, JsonElement>(), new DateTime(2026, 5, 26, 12, 31, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        // Assert
        Assert.True(result.Matched);
        Assert.True(providers.Branches[1001].Status == "Completed");
        Assert.Empty(providers.Bookmarks);
        Assert.Contains(dispatcher.Requests, request => request.Reason == BranchExecutionReason.BookmarkResumed);
    }

    private sealed record TestNode(Guid NodeId, string Name, string KindValue) : BaseNode(NodeId, Name, Array.Empty<PortDefinition>())
    {
        public override string Kind => KindValue;
    }

    private sealed class WaitThenCompleteExecutor : INodeExecutor
    {
        private int _calls;

        public string Kind => "test";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            _calls++;
            if (_calls == 1)
            {
                return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.WaitForBookmark(
                    new InboundWakeCondition("mqtt", "device-1"),
                    new Dictionary<string, JsonElement>()));
            }

            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        }
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

    private sealed class LoopBackDispatcher : IRunDispatcher
    {
        public List<BranchExecutionRequest> Requests { get; } = [];
        public Func<BranchExecutionRequest, Task>? Handler { get; set; }

        public async ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            if (Handler is not null)
            {
                await Handler(request);
            }
        }
    }

    private sealed class InMemoryProviders : IBranchProvider, IRunProvider, IRunCountersProvider, IBookmarkProvider, IHistoryEventProvider, IWorkflowDefinitionProvider, IIdempotencyKeyProvider
    {
        private readonly WorkflowDefinition _definition;
        private int _activeBranches = 1;

        public InMemoryProviders(WorkflowDefinition definition, Guid nodeId)
        {
            _definition = definition;
            Runs[42] = new RunRow
            {
                Id = 42,
                RefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                WorkflowDefinitionId = 9,
                WorkflowRefId = definition.WorkflowRefId,
                WorkflowVersion = definition.Version,
                TriggerNodeId = nodeId,
                CorrelationKey = "corr-42",
                Status = "Running",
                StartedAt = definition.CreatedAt,
                CompletedAt = null,
                CancellationRequestedAt = null,
                CancellationReason = null,
                CreditBudget = 100,
                CreatedAt = definition.CreatedAt
            };
            Branches[1001] = new BranchRow
            {
                Id = 1001,
                RefId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                RunId = 42,
                ParentBranchId = null,
                ForkCohortId = null,
                NodeId = nodeId,
                Status = "Active",
                PendingTakePort = null,
                LocalJson = "{}",
                LastOutputJson = null,
                CompensationStackJson = null,
                CreatedAt = definition.CreatedAt,
                UpdatedAt = definition.CreatedAt,
                RowVersion = [1]
            };
        }

        public Dictionary<long, BranchRow> Branches { get; } = [];
        public Dictionary<long, RunRow> Runs { get; } = [];
        public List<BookmarkRow> Bookmarks { get; } = [];
        private Dictionary<string, IdempotencyKeyRow> IdempotencyKeys { get; } = [];

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(Branches[branchId]);
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(Branches.Values.Single(branch => branch.RefId == refId));
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(branch => branch.Status == "Active").ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(branch => branch.Status == "Active").ToArray());
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct)
        {
            BranchRow updated = Branches[branchId] with { NodeId = currentNodeId, Status = status, LocalJson = localJson, LastOutputJson = lastOutputJson };
            Branches[branchId] = updated;
            return Task.FromResult(updated);
        }

        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct)
        {
            BranchRow updated = Branches[branchId] with { Status = "Completed" };
            Branches[branchId] = updated;
            return Task.FromResult(updated);
        }

        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        Task<RunRow> IRunProvider.CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        Task<RunRow> IRunProvider.GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(Runs[runId]);
        Task<RunRow> IRunProvider.GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            _activeBranches += delta;
            return Task.FromResult(_activeBranches);
        }

        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            _activeBranches -= delta;
            return Task.FromResult(_activeBranches);
        }

        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(0m);
        Task<BookmarkRow> IBookmarkProvider.CreateAsync(BookmarkRow row, CancellationToken ct)
        {
            Bookmarks.Add(row);
            return Task.FromResult(row);
        }

        Task<BookmarkRow> IBookmarkProvider.GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(Bookmarks.Single(bookmark => bookmark.RefId == refId));
        Task<BookmarkRow?> IBookmarkProvider.GetByIdAsync(long bookmarkId, CancellationToken ct) => Task.FromResult(Bookmarks.SingleOrDefault(bookmark => bookmark.Id == bookmarkId));
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Bookmarks.Where(bookmark => bookmark.MatchKey == matchKey).ToArray());
        Task<IReadOnlyCollection<BookmarkRow>> IBookmarkProvider.GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Bookmarks.Where(bookmark => bookmark.RunId == runId).ToArray());
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Bookmarks.Where(bookmark => bookmark.ExpiresAt <= nowUtc).ToArray());
        public Task DeleteAsync(Guid refId, CancellationToken ct)
        {
            Bookmarks.RemoveAll(bookmark => bookmark.RefId == refId);
            return Task.CompletedTask;
        }

        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct)
        {
            Bookmarks.RemoveAll(bookmark => bookmark.RunId == runId && bookmark.Id != excludeBookmarkId);
            return Task.CompletedTask;
        }

        public Task<int> DeleteOrphansAsync(CancellationToken ct) => Task.FromResult(0);
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct)
        {
            return Task.FromResult(new WorkflowDefinitionRow
            {
                Id = 9,
                RefId = _definition.WorkflowRefId,
                Version = _definition.Version,
                WorkspaceId = _definition.WorkspaceId,
                Name = _definition.Name,
                Description = _definition.Description,
                IsEnabled = _definition.IsEnabled,
                DefinitionJson = JsonSerializer.Serialize(_definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                PublishedBy = _definition.PublishedBy,
                CreatedAt = _definition.CreatedAt
            });
        }

        public Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task DeprecateAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct)
        {
            if (!IdempotencyKeys.TryGetValue(keyValue, out IdempotencyKeyRow? row))
            {
                row = new IdempotencyKeyRow
                {
                    Id = IdempotencyKeys.Count + 1,
                    KeyValue = keyValue,
                    RunId = runId,
                    BranchRefId = branchRefId,
                    NodeId = nodeId,
                    Attempt = attempt,
                    Status = "Pending",
                    ResultJson = null,
                    ErrorJson = null,
                    CreatedAt = _definition.CreatedAt,
                    CompletedAt = null
                };
                IdempotencyKeys[keyValue] = row;
            }

            return Task.FromResult(row);
        }

        public Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct) => Task.FromResult(IdempotencyKeys[keyValue]);
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
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")]);

        public Guid NewId() => _ids.Dequeue();
    }
}
