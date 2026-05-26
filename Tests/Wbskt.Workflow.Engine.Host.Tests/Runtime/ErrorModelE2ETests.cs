using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.SharedVariables;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class ErrorModelE2ETests
{
    [Fact]
    public async Task Parallel_branch_failure_with_compensation_finishes_partially_failed()
    {
        Guid rootNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid compensableNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid fatalNodeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Guid successNodeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        WorkflowDefinition definition = new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "error-model-e2e",
            null,
            true,
            [
                new TestNode(rootNodeId, "root", "test:fork"),
                new SendCommandActionNode(
                    compensableNodeId,
                    "compensable",
                    [new PortDefinition("next", PortDirection.Output, "Next")],
                    new SendCommandConfig("device-1", "DoThing"),
                    Compensation: new CompensationDeclaration(Guid.Empty, "test:compensate", null)),
                new TestNode(fatalNodeId, "fatal", "test:fail"),
                new TestNode(successNodeId, "success", "test:success")
            ],
            [new Edge((compensableNodeId, "next"), (fatalNodeId, "in"))],
            Array.Empty<SharedVariableDeclaration>(),
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7,
            RunCompensationOnFailure: true,
            FailFast: false);

        var state = new InMemoryState(definition, rootNodeId);
        var branchProvider = new InMemoryBranchProvider(state);
        var runProvider = new InMemoryRunProvider(state);
        var runCountersProvider = new InMemoryRunCountersProvider(state);
        var bookmarkProvider = new InMemoryBookmarkProvider(state);
        var historyEventProvider = new InMemoryHistoryEventProvider(state);
        var runDispatcher = new RecordingRunDispatcher();
        var compensationExecutor = new RecordingCompensationExecutor();
        var registry = new NodeExecutorRegistry([
            new ForkExecutor(compensableNodeId, successNodeId),
            new ContinueExecutor(NodeKind.ActionCommand, "next"),
            new FailExecutor(),
            new SuccessExecutor(),
            compensationExecutor
        ]);
        var cache = new StaticWorkflowDefinitionCache(definition);
        var providers = new NoOpProviderComposite();
        var orchestrator = new CompensationOrchestrator(runProvider, branchProvider, historyEventProvider, cache, registry, providers, state.Clock);
        var finalizer = new RunFinalizer(runProvider, runCountersProvider, branchProvider, historyEventProvider, new NoOpPendingTriggerEventDrainer(), new NullRunCompletedPublisher(), bookmarkProvider, state.Clock);
        var branchLoop = new BranchLoop(branchProvider, runProvider, runCountersProvider, bookmarkProvider, historyEventProvider, cache, registry, runDispatcher, providers, state.Clock, new SequenceIdGenerator(), finalizer, new NoOpRunCancellationService(), orchestrator);

        await branchLoop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        BranchExecutionRequest successRequest = Assert.Single(runDispatcher.Requests, request => state.Branches[request.BranchId].NodeId == successNodeId);
        BranchExecutionRequest failingRequest = Assert.Single(runDispatcher.Requests, request => state.Branches[request.BranchId].NodeId == compensableNodeId);

        await branchLoop.RunAsync(successRequest.RunId, successRequest.BranchId, successRequest.Reason, CancellationToken.None);
        await branchLoop.RunAsync(failingRequest.RunId, failingRequest.BranchId, failingRequest.Reason, CancellationToken.None);

        Assert.Equal("PartiallyFailed", state.Runs[42].Status);
        Assert.Single(compensationExecutor.Calls);
        Assert.Equal("Completed", state.Branches[successRequest.BranchId].Status);
        Assert.Equal("Failed", state.Branches[failingRequest.BranchId].Status);
        Assert.Contains(state.HistoryEvents, evt => evt.EventKind == "CompensationExecuted");
    }

    private sealed record TestNode(Guid NodeId, string Name, string KindValue) : BaseNode(NodeId, Name, Array.Empty<PortDefinition>())
    {
        public override string Kind => KindValue;
    }

    private sealed class ForkExecutor(Guid failingNodeId, Guid successNodeId) : INodeExecutor
    {
        public string Kind => "test:fork";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Fork(
            [
                new ForkSpec(failingNodeId.ToString(), new Dictionary<string, JsonElement>()),
                new ForkSpec(successNodeId.ToString(), new Dictionary<string, JsonElement>())
            ], null, new Dictionary<string, JsonElement>()));
        }
    }

    private sealed class ContinueExecutor(string kind, string outboundPort) : INodeExecutor
    {
        public string Kind => kind;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Continue(outboundPort, new Dictionary<string, JsonElement>()));
        }
    }

    private sealed class FailExecutor : INodeExecutor
    {
        public string Kind => "test:fail";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Fail("E_FAIL", "boom", false, null));
        }
    }

    private sealed class SuccessExecutor : INodeExecutor
    {
        public string Kind => "test:success";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        }
    }

    private sealed class RecordingCompensationExecutor : INodeExecutor
    {
        public string Kind => "test:compensate";
        public List<Guid> Calls { get; } = [];

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            Calls.Add(ctx.Node.NodeId);
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        }
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

    private sealed class NoOpPendingTriggerEventDrainer : IPendingTriggerEventDrainer
    {
        public Task DrainAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        private readonly Queue<Guid> _ids = new([
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000002"),
            Guid.Parse("50000000-0000-0000-0000-000000000003")]);

        public Guid NewId() => _ids.Dequeue();
    }

    private sealed class StaticWorkflowDefinitionCache(WorkflowDefinition definition) : IWorkflowDefinitionCache
    {
        public Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct) => Task.FromResult(definition);
        public void Invalidate(int workflowDefinitionId) { }
    }

    private sealed class InMemoryState
    {
        public InMemoryState(WorkflowDefinition definition, Guid rootNodeId)
        {
            Definition = definition;
            Runs[42] = new RunRow
            {
                Id = 42,
                RefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-cccccccccccc"),
                WorkflowDefinitionId = 9,
                WorkflowRefId = definition.WorkflowRefId,
                WorkflowVersion = definition.Version,
                TriggerNodeId = rootNodeId,
                CorrelationKey = "corr-42",
                Status = "Running",
                StartedAt = definition.CreatedAt,
                CompletedAt = null,
                CancellationRequestedAt = null,
                CancellationReason = null,
                CreditBudget = 100m,
                CreatedAt = definition.CreatedAt
            };
            Branches[1001] = new BranchRow
            {
                Id = 1001,
                RefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                RunId = 42,
                ParentBranchId = null,
                ForkCohortId = null,
                NodeId = rootNodeId,
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

        public WorkflowDefinition Definition { get; }
        public FixedClock Clock { get; } = new();
        public Dictionary<long, RunRow> Runs { get; } = [];
        public Dictionary<long, BranchRow> Branches { get; } = [];
        public List<HistoryEventRow> HistoryEvents { get; } = [];
        public List<BookmarkRow> Bookmarks { get; } = [];
        public int ActiveBranches { get; set; } = 1;
        public int NextBranchId { get; set; } = 1001;
    }

    private sealed class InMemoryBranchProvider(InMemoryState state) : IBranchProvider
    {
        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
        {
            BranchRow created = row with { Id = ++state.NextBranchId };
            state.Branches[created.Id] = created;
            return Task.FromResult(created);
        }

        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(state.Branches[branchId]);
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(state.Branches.Values.Single(branch => branch.RefId == refId));
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(state.Branches.Values.Where(branch => branch.RunId == runId).ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(state.Branches.Values.Where(branch => branch.RunId == runId && branch.Status == "Active").ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(state.Branches.Values.Where(branch => branch.Status == "Active").ToArray());

        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct)
        {
            BranchRow updated = state.Branches[branchId] with { NodeId = currentNodeId, Status = status, LocalJson = localJson, LastOutputJson = lastOutputJson, UpdatedAt = state.Clock.UtcNow };
            state.Branches[branchId] = updated;
            return Task.FromResult(updated);
        }

        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct)
        {
            BranchRow updated = state.Branches[branchId] with { Status = "Completed", UpdatedAt = state.Clock.UtcNow };
            state.Branches[branchId] = updated;
            return Task.FromResult(updated);
        }

        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct)
        {
            BranchRow updated = state.Branches[branchId] with { Status = "Failed", LastOutputJson = lastOutputJson, UpdatedAt = state.Clock.UtcNow };
            state.Branches[branchId] = updated;
            return Task.FromResult(updated);
        }
    }

    private sealed class InMemoryRunProvider(InMemoryState state) : IRunProvider
    {
        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(state.Runs[runId]);
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(state.Runs.Values.Single(run => run.RefId == refId));
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct) => Task.FromResult(false);

        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct)
        {
            RunRow updated = state.Runs[runId] with { Status = status, CompletedAt = completedAt };
            state.Runs[runId] = updated;
            return Task.FromResult(updated);
        }
    }

    private sealed class InMemoryRunCountersProvider(InMemoryState state) : IRunCountersProvider
    {
        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct)
        {
            return Task.FromResult(new RunCountersRow
            {
                RunId = runId,
                ActiveBranchCount = state.ActiveBranches,
                CreditsConsumed = 0m,
                UpdatedAt = state.Clock.UtcNow
            });
        }

        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            state.ActiveBranches += delta;
            return Task.FromResult(state.ActiveBranches);
        }

        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            state.ActiveBranches -= delta;
            return Task.FromResult(state.ActiveBranches);
        }

        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(0m);
    }

    private sealed class InMemoryBookmarkProvider(InMemoryState state) : IBookmarkProvider
    {
        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct)
        {
            state.Bookmarks.Add(row);
            return Task.FromResult(row);
        }

        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow?> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(state.Bookmarks.Where(bookmark => bookmark.RunId == runId).ToArray());
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
        {
            state.Bookmarks.RemoveAll(bookmark => bookmark.RunId == runId);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryHistoryEventProvider(InMemoryState state) : IHistoryEventProvider
    {
        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
        {
            long nextId = state.HistoryEvents.Count == 0 ? 1 : state.HistoryEvents.Max(evt => evt.HistoryEventId) + 1;
            foreach (HistoryEventRow evt in events)
            {
                state.HistoryEvents.Add(evt with { HistoryEventId = nextId++ });
            }

            return Task.CompletedTask;
        }

        
        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoOpProviderComposite : IProviderComposite
    {
        public IWorkflowDefinitionProvider WorkflowDefinition { get; } = new NoOpWorkflowDefinitionProvider();
        public ITriggerRegistrationProvider TriggerRegistration { get; } = new NoOpTriggerRegistrationProvider();
        public IBookmarkProvider Bookmark { get; } = new NoOpBookmarkProvider();
        public ISharedVariableProvider SharedVariable { get; } = new NoOpSharedVariableProvider();
        public IIdempotencyKeyProvider IdempotencyKey { get; } = new NoOpIdempotencyKeyProvider();
        public IPendingTriggerEventProvider PendingTriggerEvent { get; } = new NoOpPendingTriggerEventProvider();
        public IScheduledFireProvider ScheduledFire { get; } = new NoOpScheduledFireProvider();
    }

    private sealed class NoOpWorkflowDefinitionProvider : IWorkflowDefinitionProvider
    {
        public Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task DeprecateAsync(int id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoOpTriggerRegistrationProvider : ITriggerRegistrationProvider
    {
        public Task<TriggerRegistrationRow> InsertAsync(TriggerRegistrationRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetByTriggerKeyAsync(string triggerKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelAsync(string channelKind, string channelKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoOpBookmarkProvider : IBookmarkProvider
    {
        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow?> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoOpSharedVariableProvider : ISharedVariableProvider
    {
        public Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct) => throw new NotSupportedException();
        public Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoOpIdempotencyKeyProvider : IIdempotencyKeyProvider
    {
        public Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoOpPendingTriggerEventProvider : IPendingTriggerEventProvider
    {
        public Task<PendingTriggerEventRow> EnqueueAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<PendingTriggerEventRow?> DequeueNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<PendingTriggerEventRow?> DequeueNextAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunKeyAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoOpScheduledFireProvider : IScheduledFireProvider
    {
        public Task<ScheduledFireRow> InsertAsync(Guid triggerNodeId, int workflowDefinitionId, Guid workflowRefId, string cronOrInterval, DateTime nextFireAt, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ScheduledFireRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct) => throw new NotSupportedException();
        public Task<ScheduledFireRow> AdvanceNextAsync(int id, DateTime nextFireAt, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }
}



