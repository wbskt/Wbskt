using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.NodeExecutors.Triggers;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class NodeExecutorsE2ETests
{
    [Fact]
    public async Task RunAsync_logic_variable_foreach_flow_completes_and_preserves_state()
    {
        var triggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var logicNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var variableNodeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var forEachNodeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var endNodeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var bodyNodeId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        WorkflowDefinition definition = new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "phase9-e2e",
            null,
            true,
            [
                new ManualTriggerNode { NodeId = triggerNodeId, Name = "start", Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }], Config = new ManualTriggerConfig { Description = "start" } },
                new LogicGateNode { NodeId = logicNodeId, Name = "logic", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" }, new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }], Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("gate") } },
                new VariableNode { NodeId = variableNodeId, Name = "variable", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }], Config = new VariableConfig { Scope = VariableScope.Local, Op = VariableOperation.Set, Var = "mode", Value = JsonSerializer.SerializeToElement("auto") } },
                new ForEachNode { NodeId = forEachNodeId, Name = "foreach", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "body", Direction = PortDirection.Output, Label = "Body" }, new PortDefinition { PortId = "done", Direction = PortDirection.Output, Label = "Done" }], Config = new ForEachConfig { Collection = "items" } },
                new LogicGateNode { NodeId = endNodeId, Name = "end", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" }, new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }], Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("true") } },
                new LogicGateNode { NodeId = bodyNodeId, Name = "body", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" }, new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }], Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("true") } }
            ],
            [
                new Edge((triggerNodeId, "default"), (logicNodeId, "in")),
                new Edge((logicNodeId, "true"), (variableNodeId, "in")),
                new Edge((variableNodeId, "default"), (forEachNodeId, "in")),
                // ForEach is sequential: the body returns to the loop node, which hands out the next
                // item or leaves via "done". The back-edge is what makes it a loop.
                new Edge((forEachNodeId, "body"), (bodyNodeId, "in")),
                new Edge((bodyNodeId, "true"), (forEachNodeId, "in")),
                new Edge((forEachNodeId, "done"), (endNodeId, "in"))
            ],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);

        var providers = new InMemoryProviders(definition, triggerNodeId);
        var dispatcher = new RecordingRunDispatcher();
        var finalizer = new RecordingRunFinalizer();
        var loop = new BranchLoop(
            providers,
            providers,
            providers,
            providers,
            providers,
            new StubWorkflowDefinitionCache(definition),
            new NodeExecutorRegistry([
                new PassthroughTriggerExecutor(NodeKind.TriggerManual, new FixedClock()),
                new LogicNodeExecutor(new ExpressionEvaluator(new SystemClock())),
                new VariableNodeExecutor(new NoOpSharedVariableProvider(), new ExpressionEvaluator(new SystemClock())),
                new ForEachNodeExecutor(new ExpressionEvaluator(new SystemClock()))
            ]),
            dispatcher,
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator(),
            finalizer);

        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        foreach (BranchExecutionRequest request in dispatcher.Requests.ToArray())
        {
            await loop.RunAsync(request.RunId, request.BranchId, request.Reason, CancellationToken.None);
        }

        // Sequential: one branch walks the whole collection - nothing is forked.
        Assert.Empty(providers.CreatedBranches);
        Assert.Equal("Completed", providers.Branches[1001].Status);
        Assert.Equal([42L], finalizer.RunIds);

        // The loop node is entered once per item plus once more to leave via "done", and the body
        // node runs once per item.
        Assert.Equal(4, providers.HistoryEvents.Count(evt => evt.EventKind == "NodeStarted" && evt.NodeId == forEachNodeId));
        Assert.Equal(3, providers.HistoryEvents.Count(evt => evt.EventKind == "NodeStarted" && evt.NodeId == bodyNodeId));

        // Items were handed out in order.
        string[] itemsSeen = providers.HistoryEvents
            .Where(evt => evt.EventKind == "NodeCompleted" && evt.NodeId == forEachNodeId && evt.PayloadJson is not null)
            .Select(evt => JsonDocument.Parse(evt.PayloadJson!).RootElement)
            .Where(payload => payload.GetProperty("port").GetString() == "body")
            .Select(payload => payload.GetProperty("output").GetProperty("item").GetString()!)
            .ToArray();
        Assert.Equal(["x", "y", "z"], itemsSeen);

        // Final state: earlier nodes' writes survive, and the iterator is cleared on the way out.
        JsonElement finalState = JsonDocument.Parse(providers.Branches[1001].LocalJson).RootElement;
        Assert.Equal("auto", finalState.GetProperty("mode").GetString());
        Assert.False(finalState.TryGetProperty(ForEachNodeExecutor.IteratorKey(forEachNodeId), out _));
    }

    private sealed class InMemoryProviders : IBranchProvider, IRunProvider, IRunCountersProvider, IBookmarkProvider, IHistoryEventProvider
    {
        private readonly WorkflowDefinition _definition;
        private int _activeBranches = 1;
        private int _nextBranchId = 2000;

        public InMemoryProviders(WorkflowDefinition definition, Guid triggerNodeId)
        {
            _definition = definition;
            Runs[42] = new RunRow
            {
                Id = 42,
                RefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                WorkflowDefinitionId = 9,
                WorkflowRefId = definition.WorkflowRefId,
                WorkflowVersion = definition.Version,
                TriggerNodeId = triggerNodeId,
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
                NodeId = triggerNodeId,
                Status = "Active",
                PendingTakePort = null,
                LocalJson = JsonSerializer.Serialize(new Dictionary<string, JsonElement>
                {
                    ["gate"] = JsonSerializer.SerializeToElement(true),
                    ["items"] = JsonSerializer.SerializeToElement(new[] { "x", "y", "z" })
                }),
                LastOutputJson = null,
                CompensationStackJson = null,
                CreatedAt = definition.CreatedAt,
                UpdatedAt = definition.CreatedAt,
                RowVersion = [1]
            };
        }

        public Dictionary<long, BranchRow> Branches { get; } = [];
        public Dictionary<long, RunRow> Runs { get; } = [];
        public List<BranchRow> CreatedBranches { get; } = [];
        public List<HistoryEventRow> HistoryEvents { get; } = [];

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
        {
            BranchRow created = row with { Id = ++_nextBranchId };
            Branches[created.Id] = created;
            CreatedBranches.Add(created);
            return Task.FromResult(created);
        }

        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(Branches[branchId]);
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(Branches.Values.Single(row => row.RefId == refId));
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(row => row.RunId == runId).ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(row => row.RunId == runId && row.Status == "Active").ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(row => row.Status == "Active").ToArray());

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
        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        Task<RunRow> IRunProvider.GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(Runs[runId]);
        Task<RunRow> IRunProvider.GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => Task.FromResult((long)Runs.Values.Count(run => run.Status == status));

        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct)
        {
            RunRow updated = Runs[runId] with { Status = status, CompletedAt = completedAt };
            Runs[runId] = updated;
            return Task.FromResult((true, updated));
        }

        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct)
        {
            return Task.FromResult(new RunCountersRow { RunId = runId, ActiveBranchCount = _activeBranches, CreditsConsumed = 0m, UpdatedAt = _definition.CreatedAt });
        }

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

        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => Task.FromResult((long)_activeBranches);
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(0m);
        public Task<bool> TryChargeAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(true);
        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
        Task<BookmarkRow> IBookmarkProvider.GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        Task<BookmarkRow> IBookmarkProvider.GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        Task<IReadOnlyCollection<BookmarkRow>> IBookmarkProvider.GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryClaimAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountAsync(CancellationToken ct) => Task.FromResult(0L);
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
        {
            HistoryEvents.AddRange(events);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubWorkflowDefinitionCache(WorkflowDefinition definition) : IWorkflowDefinitionCache
    {
        public Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct)
        {
            _ = workflowDefinitionId;
            _ = ct;
            return Task.FromResult(definition);
        }

        public void Invalidate(int workflowDefinitionId)
        {
            _ = workflowDefinitionId;
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

    private sealed class RecordingRunFinalizer : IRunFinalizer
    {
        public List<long> RunIds { get; } = [];

        public Task FinalizeAsync(long runId, CancellationToken ct)
        {
            RunIds.Add(runId);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
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

    private sealed class NoOpSharedVariableProvider : ISharedVariableProvider
    {
        public Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct) => throw new NotSupportedException();
        public Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct) => throw new NotSupportedException();
    }
}






