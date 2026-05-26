using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class BranchLoopEndToEndTests
{
    [Fact]
    public async Task RunAsync_manual_trigger_logic_happy_path_completes_branch_and_finalizes_run()
    {
        // Arrange
        var triggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var logicNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        WorkflowDefinition definition = new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            9,
            "happy-path",
            null,
            true,
            [
                new ManualTriggerNode(
                    triggerNodeId,
                    "start",
                    [new PortDefinition("out", PortDirection.Output, "Out")],
                    new ManualTriggerConfig("manual start")),
                new LogicGateNode(
                    logicNodeId,
                    "logic",
                    [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("next", PortDirection.Output, "Next")],
                    new LogicGateConfig("true"))
            ],
            [new Edge((triggerNodeId, "out"), (logicNodeId, "in"))],
            [],
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            7);
        var providers = new InMemoryProviders(definition, triggerNodeId);
        var finalizer = new RecordingRunFinalizer();
        var loop = new BranchLoop(
            providers,
            providers,
            providers,
            providers,
            providers,
            new WorkflowDefinitionCache(new MemoryCache(new MemoryCacheOptions()), providers),
            new NodeExecutorRegistry([
                new ManualTriggerExecutor(),
                new LogicPassThroughExecutor()
            ]),
            new ChannelRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator(),
            finalizer);

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal(
            ["BranchStarted", "NodeStarted", "NodeCompleted", "NodeStarted", "NodeCompleted", "BranchCompleted"],
            providers.HistoryEvents.Select(evt => evt.EventKind));
        Assert.Equal("Completed", providers.Branches[1001].Status);
        Assert.Equal([42L], finalizer.RunIds);
    }

    private sealed class ManualTriggerExecutor : INodeExecutor
    {
        public string Kind => NodeKind.TriggerManual;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Continue("out", new Dictionary<string, JsonElement>()));
        }
    }

    private sealed class LogicPassThroughExecutor : INodeExecutor
    {
        public string Kind => NodeKind.ControlLogic;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Continue("next", new Dictionary<string, JsonElement>()));
        }
    }

    private sealed class InMemoryProviders : IBranchProvider, IRunProvider, IRunCountersProvider, IBookmarkProvider, IHistoryEventProvider, IWorkflowDefinitionProvider
    {
        private readonly WorkflowDefinition _definition;
        private int _activeBranches = 1;

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

        public List<HistoryEventRow> HistoryEvents { get; } = [];

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        Task<BranchRow> IBranchProvider.GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(Branches[branchId]);
        Task<BranchRow> IBranchProvider.GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(Branches.Values.Single(row => row.RefId == refId));
        Task<IReadOnlyCollection<BranchRow>> IBranchProvider.GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(row => row.Status == "Active").ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(Branches.Values.Where(row => row.Status == "Active").ToArray());

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
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
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

        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => throw new NotSupportedException();
        Task<BookmarkRow> IBookmarkProvider.GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        Task<BookmarkRow?> IBookmarkProvider.GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        Task<IReadOnlyCollection<BookmarkRow>> IBookmarkProvider.GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();

        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
        {
            HistoryEvents.AddRange(events);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyCollection<HistoryEventRow>>(HistoryEvents);
        }

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
        public Guid NewId() => Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
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
}
