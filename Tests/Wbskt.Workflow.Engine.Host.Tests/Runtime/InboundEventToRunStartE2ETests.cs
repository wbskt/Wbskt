using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Models.SharedVariables;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Triggers;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class InboundEventToRunStartE2ETests
{
    [Fact]
    public async Task HandleAsync_device_event_starts_run_and_completes_branch_loop()
    {
        // Arrange
        var definition = CreateDefinition();
        var providers = new InMemoryRuntime(definition);
        var runDispatcher = new RecordingRunDispatcher();
        var bookmarkResumer = new BookmarkResumer(providers, providers, providers, runDispatcher);
        var triggerDispatcher = new TriggerDispatcher(
            new CorrelationKeyResolver(),
            bookmarkResumer,
            providers,
            new TriggerConcurrencyEnforcer(providers, providers),
            new NoOpRunCancellationService(),
            new RunStarter(providers, providers, providers, providers, providers, new CorrelationKeyResolver(), new NullRunStartedPublisher(), new FixedClock(), new SequenceIdGenerator()),
            runDispatcher,
            providers);
        var inboundHub = new InboundHub(triggerDispatcher, new FixedClock(), new TestLogger<InboundHub>());
        var branchLoop = new BranchLoop(
            providers,
            providers,
            providers,
            providers,
            providers,
            new WorkflowDefinitionCache(new MemoryCache(new MemoryCacheOptions()), providers),
            new NodeExecutorRegistry([
                new DeviceTriggerExecutor(new FixedClock()),
                new LogicPassThroughExecutor()
            ]),
            runDispatcher,
            providers,
            new FixedClock(),
            new SequenceIdGenerator(),
            new CompletingRunFinalizer(providers));
        InboundEvent evt = new(
            "client",
            ["client:client-serial-1:telemetry"],
            "evt-1",
            new Dictionary<string, JsonElement>
            {
                ["clientRefId"] = JsonSerializer.SerializeToElement("client-serial-1"),
                ["messageType"] = JsonSerializer.SerializeToElement("telemetry")
            },
            default);

        // Act
        TriggerDispatchResult dispatchResult = await inboundHub.HandleAsync(evt, CancellationToken.None);
        BranchExecutionRequest request = Assert.Single(runDispatcher.Requests);
        await branchLoop.RunAsync(request.RunId, request.BranchId, request.Reason, CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, dispatchResult.Outcome);
        Assert.Equal("Completed", providers.Runs[request.RunId].Status);
        Assert.Equal(
            ["RunStarted", "BranchStarted", "NodeStarted", "NodeCompleted", "NodeStarted", "NodeCompleted", "NodeStarted", "NodeCompleted", "BranchCompleted"],
            providers.HistoryEvents.Select(evtRow => evtRow.EventKind));
        Assert.Empty(providers.Bookmarks);
        Assert.Empty(providers.PendingEvents);
    }

    private static WorkflowDefinition CreateDefinition()
    {
        Guid triggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid logicNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid terminalNodeId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        return new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            3,
            9,
            "e2e",
            null,
            true,
            [
                new ClientTriggerNode { NodeId = triggerNodeId, Name = "client", Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }], Config = new ClientTriggerConfig { ClientRef = "client-serial-1", Type = "telemetry", CorrelationKey = null, ConcurrencyPolicy = WorkflowConcurrencyPolicy.AllowParallel } },
                new LogicGateNode { NodeId = logicNodeId, Name = "logic", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "next", Direction = PortDirection.Output, Label = "Next" }], Config = new LogicGateConfig { Condition = "true" } },
                new LogicGateNode { NodeId = terminalNodeId, Name = "terminal", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "next", Direction = PortDirection.Output, Label = "Next" }], Config = new LogicGateConfig { Condition = "true" } }
            ],
            [new Edge((triggerNodeId, "default"), (logicNodeId, "in")), new Edge((logicNodeId, "next"), (terminalNodeId, "in"))],
            Array.Empty<SharedVariableDeclaration>(),
            new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc),
            7);
    }

    private sealed class LogicPassThroughExecutor : INodeExecutor
    {
        public string Kind => NodeKind.ControlLogic;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Continue("next", new Dictionary<string, JsonElement>()));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        private readonly Queue<Guid> _ids = new([
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000002")]);

        public Guid NewId() => _ids.Count > 0 ? _ids.Dequeue() : Guid.NewGuid();
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

    private sealed class CompletingRunFinalizer(InMemoryRuntime runtime) : IRunFinalizer
    {
        public Task FinalizeAsync(long runId, CancellationToken ct)
        {
            RunRow run = runtime.Runs[runId];
            runtime.Runs[runId] = run with { Status = "Completed", CompletedAt = runtime.Clock.UtcNow };
            return Task.CompletedTask;
        }
    }

    private sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private sealed class InMemoryRuntime :
        IBranchProvider,
        IRunProvider,
        IRunCountersProvider,
        IBookmarkProvider,
        IHistoryEventProvider,
        IWorkflowDefinitionProvider,
        ITriggerRegistrationProvider,
        IProviderComposite,
        IIdempotencyKeyProvider,
        IPendingTriggerEventProvider,
        IScheduledFireProvider,
        ISharedVariableProvider
    {
        private readonly WorkflowDefinition _definition;
        private int _nextRunId = 100;
        private int _nextBranchId = 1000;
        private readonly Dictionary<long, int> _activeBranches = [];
        public Task<Wbskt.Models.IPagedList<WorkflowDefinitionRow>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct) => Task.FromResult<Wbskt.Models.IPagedList<WorkflowDefinitionRow>>(new Wbskt.Models.PagedList<WorkflowDefinitionRow>(Array.Empty<WorkflowDefinitionRow>(), 0));

        public InMemoryRuntime(WorkflowDefinition definition)
        {
            _definition = definition;
        }

        public FixedClock Clock { get; } = new();
        public Dictionary<long, RunRow> Runs { get; } = [];
        public Dictionary<long, BranchRow> Branches { get; } = [];
        public List<BookmarkRow> Bookmarks { get; } = [];
        public List<HistoryEventRow> HistoryEvents { get; } = [];
        public List<PendingTriggerEventRow> PendingEvents { get; } = [];

        public IWorkflowDefinitionProvider WorkflowDefinition => this;
        public ITriggerRegistrationProvider TriggerRegistration => this;
        public IBookmarkProvider Bookmark => this;
        public ISharedVariableProvider SharedVariable => this;
        public IIdempotencyKeyProvider IdempotencyKey => this;
        public IPendingTriggerEventProvider PendingTriggerEvent => this;
        public IScheduledFireProvider ScheduledFire => this;

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct)
        {
            RunRow created = row with { Id = ++_nextRunId };
            Runs[created.Id] = created;

            // Run_Create seeds the RunCounters row at 0; RunStarter increments it
            // for the initial branch (matches the real stored procedure).
            _activeBranches[created.Id] = 0;
            return Task.FromResult(created);
        }

        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult<int?>(Runs.Values.Single(run => run.RefId == refId).Id);
        Task<RunRow> IRunProvider.GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(Runs[runId]);
        Task<RunRow> IRunProvider.GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(Runs.Values.Single(run => run.RefId == refId));
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<RunRow>>(Runs.Values.Where(run => run.WorkflowRefId == workflowRefId && run.CorrelationKey == correlationKey && run.Status == "Running").ToArray());
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<RunRow>>(Runs.Values.Where(run => run.WorkflowRefId == workflowRefId && run.TriggerNodeId == triggerNodeId && run.CorrelationKey == correlationKey && (run.Status == "Running" || run.Status == "Cancelling" || run.Status == "Failing")).ToArray());
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct)
        {
            RunRow run = Runs.Values.Single(candidate => candidate.RefId == refId);
            RunRow updated = run with { Status = status, CompletedAt = completedAt, CancellationRequestedAt = cancellationRequestedAt, CancellationReason = cancellationReason };
            Runs[updated.Id] = updated;
            return Task.FromResult(updated);
        }

        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => Task.FromResult((long)Runs.Values.Count(run => run.Status == status));

        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct)
        {
            RunRow run = Runs[runId];
            if (!string.Equals(run.Status, fromStatus, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            Runs[runId] = run with { Status = toStatus };
            return Task.FromResult(true);
        }

        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct)
        {
            RunRow updated = Runs[runId] with { Status = status, CompletedAt = completedAt };
            Runs[runId] = updated;
            return Task.FromResult(updated);
        }
 
        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
        {
            BranchRow created = row with { Id = ++_nextBranchId };
            Branches[created.Id] = created;
            return Task.FromResult(created);
        }

        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(Branches[branchId]);
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(Branches.Values.Single(branch => branch.RefId == refId));
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(branch => branch.RunId == runId).ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(branch => branch.RunId == runId && branch.Status == "Active").ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(branch => branch.Status == "Active").ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
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

        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct)
        {
            return Task.FromResult(new RunCountersRow
            {
                RunId = runId,
                ActiveBranchCount = _activeBranches.GetValueOrDefault(runId),
                CreditsConsumed = 0m,
                UpdatedAt = Clock.UtcNow
            });
        }
 
        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            _activeBranches[runId] = _activeBranches.GetValueOrDefault(runId) + delta;
            return Task.FromResult(_activeBranches[runId]);
        }

        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            _activeBranches[runId] = _activeBranches.GetValueOrDefault(runId) - delta;
            return Task.FromResult(_activeBranches[runId]);
        }

        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => Task.FromResult(_activeBranches.Values.Sum(value => (long)value));
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(0m);

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct)
        {
            Bookmarks.Add(row);
            return Task.FromResult(row);
        }

        Task<BookmarkRow> IBookmarkProvider.GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        Task<BookmarkRow> IBookmarkProvider.GetByIdAsync(long bookmarkId, CancellationToken ct) => Task.FromResult(Bookmarks.Single(bookmark => bookmark.Id == bookmarkId));
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Bookmarks.Where(bookmark => bookmark.MatchKey == matchKey).ToArray());
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeysAsync(IReadOnlyCollection<string> matchKeys, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Bookmarks.Where(bookmark => matchKeys.Contains(bookmark.MatchKey)).ToArray());
        Task<IReadOnlyCollection<BookmarkRow>> IBookmarkProvider.GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Bookmarks.Where(bookmark => bookmark.RunId == runId).ToArray());
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(Array.Empty<BookmarkRow>());
        public Task DeleteAsync(Guid refId, CancellationToken ct)
        {
            Bookmarks.RemoveAll(bookmark => bookmark.RefId == refId);
            return Task.CompletedTask;
        }
        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct) => Task.CompletedTask;
        public Task<long> CountAsync(CancellationToken ct) => Task.FromResult((long)Bookmarks.Count);
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => Task.FromResult(0);
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
        {
            Bookmarks.RemoveAll(bookmark => bookmark.RunId == runId);
            return Task.CompletedTask;
        }

        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
        {
            HistoryEvents.AddRange(events);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => Task.FromResult<int?>(_definition.WorkflowRefId == refId && _definition.Version == version ? 42 : null);
        public Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct) => Task.FromResult(ToRow(id));
        public Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => Task.FromResult(ToRow(42));
        public Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(ToRow(42));
        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task DeprecateAsync(int id, CancellationToken ct) => throw new NotSupportedException();

        public Task<TriggerRegistrationRow> InsertAsync(TriggerRegistrationRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetByTriggerKeyAsync(string triggerKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelKeysAsync(string channelKind, IReadOnlyCollection<string> channelKeys, CancellationToken ct)
        {
            if (channelKind == "client" && channelKeys.Contains("client:client-serial-1:telemetry"))
            {
                return Task.FromResult<IReadOnlyCollection<TriggerRegistrationRow>>([
                    new TriggerRegistrationRow
                    {
                        Id = 1,
                        WorkflowDefinitionId = 42,
                        WorkflowRefId = _definition.WorkflowRefId,
                        WorkflowVersion = _definition.Version,
                        TriggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        TriggerKind = "client",
                        TriggerKey = "client:client-serial-1:telemetry",
                        CorrelationExpression = null,
                        ConcurrencyPolicy = "AllowParallel",
                        FilterExpression = null,
                        CreatedAt = _definition.CreatedAt
                    }
                ]);
            }

            return Task.FromResult<IReadOnlyCollection<TriggerRegistrationRow>>(Array.Empty<TriggerRegistrationRow>());
        }
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => Task.CompletedTask;

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
                CreatedAt = Clock.UtcNow,
                CompletedAt = null
            });
        }
        public Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct) => Task.FromResult<IdempotencyKeyRow>(null!);
        public Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct) => throw new NotSupportedException();

        public Task<PendingTriggerEventRow> EnqueueAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<PendingTriggerEventRow?> DequeueNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => Task.FromResult<PendingTriggerEventRow?>(null);
        public Task<PendingTriggerEventRow?> DequeueNextAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => Task.FromResult<PendingTriggerEventRow?>(null);
        public Task DeleteAllByRunKeyAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => Task.CompletedTask;
        public Task<long> CountAllAsync(CancellationToken ct) => Task.FromResult((long)PendingEvents.Count);
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<ScheduledFireRow> InsertAsync(Guid triggerNodeId, int workflowDefinitionId, Guid workflowRefId, string cronOrInterval, DateTime nextFireAt, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ScheduledFireRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct) => throw new NotSupportedException();
        public Task<ScheduledFireRow> AdvanceNextAsync(int id, DateTime nextFireAt, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();

        public Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct) => throw new NotSupportedException();
        public Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct) => throw new NotSupportedException();

        private WorkflowDefinitionRow ToRow(int id)
        {
            return new WorkflowDefinitionRow
            {
                Id = id,
                RefId = _definition.WorkflowRefId,
                Version = _definition.Version,
                WorkspaceId = _definition.WorkspaceId,
                Name = _definition.Name,
                Description = _definition.Description,
                IsEnabled = _definition.IsEnabled,
                DefinitionJson = JsonSerializer.Serialize(_definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                PublishedBy = _definition.PublishedBy,
                CreatedAt = _definition.CreatedAt
            };
        }
    }
}






