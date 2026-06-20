using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class CompensationOrchestratorTests
{
    [Fact]
    public async Task Run_schedules_compensations_in_reverse_order()
    {
        var harness = new CompensationHarness(
            CreateDefinition(),
            [CreateHistoryEvent(1, Guid.Parse("11111111-1111-1111-1111-111111111111"), 1), CreateHistoryEvent(2, Guid.Parse("22222222-2222-2222-2222-222222222222"), 2)],
            new RecordingExecutor());

        await harness.Orchestrator.RunAsync(42, 1001, CancellationToken.None);

        Assert.Equal(["action:email", "action:command"], harness.Executor.ExecutedKinds);
        Assert.Equal([2, 1], harness.Executor.LocalStateValues);
        Assert.Equal(2, harness.InsertedCompensationEvents.Count(evt => evt.EventKind == "CompensationExecuted"));
    }

    [Fact]
    public async Task Run_continues_on_individual_compensation_failure()
    {
        var harness = new CompensationHarness(
            CreateDefinition(),
            [CreateHistoryEvent(1, Guid.Parse("11111111-1111-1111-1111-111111111111"), 1), CreateHistoryEvent(2, Guid.Parse("22222222-2222-2222-2222-222222222222"), 2)],
            new RecordingExecutor(throwOnKinds: ["action:command"]));

        await harness.Orchestrator.RunAsync(42, 1001, CancellationToken.None);

        Assert.Contains("action:command", harness.Executor.ExecutedKinds);
        Assert.Contains("action:email", harness.Executor.ExecutedKinds);
        Assert.Single(harness.InsertedCompensationEvents, evt => evt.EventKind == "CompensationExecuted");
    }

    [Fact]
    public async Task Run_restores_state_from_output_property()
    {
        var harness = new CompensationHarness(
            CreateDefinition(),
            [CreateHistoryEventWithOutput(1, Guid.Parse("11111111-1111-1111-1111-111111111111"), 42)],
            new RecordingExecutor());

        await harness.Orchestrator.RunAsync(42, 1001, CancellationToken.None);

        Assert.Equal(["action:command"], harness.Executor.ExecutedKinds);
        Assert.Equal([42], harness.Executor.LocalStateValues);
    }

    [Fact]
    public async Task Run_skips_nodes_without_compensation_action()
    {
        WorkflowDefinition definition = new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "compensation",
            null,
            true,
            [
                new SendCommandActionNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "first", Ports = [], Config = new SendCommandConfig { DeviceRef = "device-1", Command = "DoThing" }, Compensation = new CompensationDeclaration { NodeId = Guid.Empty, Kind = "action:command", Config = null } },
                new SendCommandActionNode { NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "second", Ports = [], Config = new SendCommandConfig { DeviceRef = "device-1", Command = "DoThing" } }
            ],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var harness = new CompensationHarness(
            definition,
            [CreateHistoryEvent(1, Guid.Parse("11111111-1111-1111-1111-111111111111"), 1), CreateHistoryEvent(2, Guid.Parse("22222222-2222-2222-2222-222222222222"), 2)],
            new RecordingExecutor());

        await harness.Orchestrator.RunAsync(42, 1001, CancellationToken.None);

        Assert.Single(harness.Executor.ExecutedKinds);
        Assert.Equal("action:command", harness.Executor.ExecutedKinds.Single());
    }

    private static WorkflowDefinition CreateDefinition()
    {
        return new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "compensation",
            null,
            true,
            [
                new SendCommandActionNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "first", Ports = [], Config = new SendCommandConfig { DeviceRef = "device-1", Command = "DoThing" }, Compensation = new CompensationDeclaration { NodeId = Guid.Empty, Kind = "action:command", Config = null } },
                new SendCommandActionNode { NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "second", Ports = [], Config = new SendCommandConfig { DeviceRef = "device-1", Command = "DoThing" }, Compensation = new CompensationDeclaration { NodeId = Guid.Empty, Kind = "action:email", Config = null } }
            ],
            [],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
    }

    private static HistoryEventRow CreateHistoryEvent(long id, Guid nodeId, int localValue)
    {
        return new HistoryEventRow
        {
            HistoryEventId = id,
            RunId = 42,
            BranchRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            NodeId = nodeId,
            EventKind = "NodeCompleted",
            Severity = "Info",
            PayloadJson = JsonSerializer.Serialize(new { localState = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(localValue) } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Timestamp = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private static HistoryEventRow CreateHistoryEventWithOutput(long id, Guid nodeId, int localValue)
    {
        return new HistoryEventRow
        {
            HistoryEventId = id,
            RunId = 42,
            BranchRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            NodeId = nodeId,
            EventKind = "NodeCompleted",
            Severity = "Info",
            PayloadJson = JsonSerializer.Serialize(new { output = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(localValue) } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Timestamp = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed class CompensationHarness
    {
        public CompensationHarness(WorkflowDefinition definition, IReadOnlyCollection<HistoryEventRow> history, RecordingExecutor executor)
        {
            Executor = executor;
            HistoryProvider = new RecordingHistoryEventProvider(history);
            Orchestrator = new CompensationOrchestrator(
                new StubRunProvider(),
                new StubBranchProvider(),
                HistoryProvider,
                new StubWorkflowDefinitionCache(definition),
                new StubNodeExecutorRegistry(executor),
                new StubProviderComposite(),
                new FixedClock());
        }

        public RecordingExecutor Executor { get; }
        public RecordingHistoryEventProvider HistoryProvider { get; }
        public IReadOnlyCollection<HistoryEventRow> InsertedCompensationEvents => HistoryProvider.InsertedEvents;
        public CompensationOrchestrator Orchestrator { get; }
    }

    private sealed class RecordingExecutor(IReadOnlyCollection<string>? throwOnKinds = null) : INodeExecutor
    {
        private readonly HashSet<string> _throwOnKinds = new(throwOnKinds ?? [], StringComparer.Ordinal);

        public List<string> ExecutedKinds { get; } = [];
        public List<int> LocalStateValues { get; } = [];
        public string Kind => "ignored";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            ExecutedKinds.Add(ctx.Node.Kind);
            LocalStateValues.Add(ctx.Branch.LocalState["value"].GetInt32());
            if (_throwOnKinds.Contains(ctx.Node.Kind))
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        }
    }

    private sealed class StubRunProvider : IRunProvider
    {
        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(new RunRow
        {
            Id = 42,
            RefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowDefinitionId = 9,
            WorkflowRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            WorkflowVersion = 1,
            TriggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CorrelationKey = "corr-42",
            Status = "Running",
            StartedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 100m,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
        });
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubBranchProvider : IBranchProvider
    {
        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(new BranchRow
        {
            Id = 1001,
            RefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            RunId = 42,
            ParentBranchId = null,
            ForkCohortId = null,
            NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Status = "Failed",
            PendingTakePort = null,
            LocalJson = "{}",
            LastOutputJson = null,
            CompensationStackJson = null,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            RowVersion = [1]
        });
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingHistoryEventProvider(IReadOnlyCollection<HistoryEventRow> existingEvents) : IHistoryEventProvider
    {
        public List<HistoryEventRow> InsertedEvents { get; } = [];

        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
        {
            InsertedEvents.AddRange(events);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct)
        {
            IReadOnlyCollection<HistoryEventRow> events = existingEvents
                .Where(evt => evt.RunId == runId && evt.HistoryEventId > afterEventId)
                .OrderBy(evt => evt.HistoryEventId)
                .Take(pageSize)
                .ToArray();
            return Task.FromResult(events);
        }

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubWorkflowDefinitionCache(WorkflowDefinition definition) : IWorkflowDefinitionCache
    {
        public Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct) => Task.FromResult(definition);
        public void Invalidate(int workflowDefinitionId) { }
    }

    private sealed class StubNodeExecutorRegistry(INodeExecutor executor) : INodeExecutorRegistry
    {
        public INodeExecutor For(string kind) => executor;
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
}






