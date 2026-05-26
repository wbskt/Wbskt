using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class BranchLoopTests
{
    [Fact]
    public async Task RunAsync_executes_two_Continue_nodes_then_Terminal()
    {
        // Arrange
        var firstNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var thirdNodeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var definition = TestWorkflowDefinition.Create(
            new TestNode(firstNodeId, "start", "test"),
            new TestNode(secondNodeId, "middle", "test"),
            new TestNode(thirdNodeId, "end", "test"),
            new Edge((firstNodeId, "next"), (secondNodeId, "in")),
            new Edge((secondNodeId, "next"), (thirdNodeId, "in")));
        var branchProvider = new RecordingBranchProvider(firstNodeId);
        var runProvider = new StubRunProvider();
        var registry = new StubNodeExecutorRegistry(
            new ScriptedExecutor(
                new NodeExecutionResult.Continue("next", CreatePatch("step", 1)),
                new NodeExecutionResult.Continue("next", CreatePatch("step", 2)),
                new NodeExecutionResult.Terminal(BranchTerminalReason.Completed)));
        var historyProvider = new RecordingHistoryEventProvider();
        var loop = new BranchLoop(
            branchProvider,
            runProvider,
            new StubRunCountersProvider(),
            new RecordingBookmarkProvider(),
            historyProvider,
            new StubWorkflowDefinitionCache(definition),
            registry,
            new RecordingRunDispatcher(),
            new StubProviderComposite(),
            new FixedClock(),
            new SequentialIdGenerator());

        // Act
        await loop.RunAsync(42, 1001, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        // Assert
        Assert.Equal([secondNodeId, thirdNodeId], branchProvider.PointerUpdates.Select(update => update.CurrentNodeId));
        Assert.True(branchProvider.SetCompletedCalled);
        Assert.Equal(
            ["BranchStarted", "NodeStarted", "NodeCompleted", "NodeStarted", "NodeCompleted", "NodeStarted", "NodeCompleted", "BranchCompleted"],
            historyProvider.Events.Select(evt => evt.EventKind));
    }

    private static IReadOnlyDictionary<string, JsonElement> CreatePatch(string name, int value)
    {
        return new Dictionary<string, JsonElement>
        {
            [name] = JsonDocument.Parse(value.ToString()).RootElement.Clone()
        };
    }

    private sealed record TestNode(Guid NodeId, string Name, string KindValue) : BaseNode(NodeId, Name, Array.Empty<PortDefinition>())
    {
        public override string Kind => KindValue;
    }

    private sealed class TestWorkflowDefinition
    {
        public static WorkflowDefinition Create(TestNode first, TestNode second, TestNode third, Edge firstEdge, Edge secondEdge)
        {
            return new WorkflowDefinition(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                1,
                9,
                "branch-loop",
                null,
                true,
                [first, second, third],
                [firstEdge, secondEdge],
                [],
                new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                7);
        }
    }

    private sealed class ScriptedExecutor(params NodeExecutionResult[] results) : INodeExecutor
    {
        private readonly Queue<NodeExecutionResult> _results = new(results);

        public string Kind => "test";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class StubNodeExecutorRegistry(INodeExecutor executor) : INodeExecutorRegistry
    {
        public INodeExecutor For(string kind) => executor;
    }

    private sealed class StubWorkflowDefinitionCache(WorkflowDefinition definition) : IWorkflowDefinitionCache
    {
        public Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct) => Task.FromResult(definition);

        public void Invalidate(int workflowDefinitionId)
        {
        }
    }

    private sealed class RecordingBranchProvider(Guid initialNodeId) : IBranchProvider
    {
        private BranchRow _row = new()
        {
            Id = 1001,
            RefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
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
        };

        public List<(long BranchId, Guid CurrentNodeId)> PointerUpdates { get; } = [];

        public bool SetCompletedCalled { get; private set; }

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
        {
            _row = row;
            return Task.FromResult(_row);
        }

        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct)
        {
            _row = row;
            return Task.FromResult(_row);
        }

        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct)
        {
            return Task.FromResult(_row);
        }

        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct)
        {
            return Task.FromResult(_row);
        }

        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyCollection<BranchRow>>([_row]);
        }

        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyCollection<BranchRow>>([_row]);
        }

        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyCollection<BranchRow>>([_row]);
        }

        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct)
        {
            PointerUpdates.Add((branchId, currentNodeId));
            _row = _row with { NodeId = currentNodeId, Status = status, LocalJson = localJson, LastOutputJson = lastOutputJson };
            return Task.FromResult(_row);
        }

        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct)
        {
            SetCompletedCalled = true;
            _row = _row with { Status = "Completed" };
            return Task.FromResult(_row);
        }

        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct)
        {
            _row = _row with { Status = "Failed", LastOutputJson = lastOutputJson };
            return Task.FromResult(_row);
        }
    }

    private sealed class StubRunProvider : IRunProvider
    {
        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();

        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();

        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct)
        {
            return Task.FromResult(new RunRow
            {
                Id = 42,
                RefId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                WorkflowDefinitionId = 9,
                WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
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

        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubRunCountersProvider : IRunCountersProvider
    {
        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => Task.FromResult(0);

        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => Task.FromResult(0);

        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(0m);
    }

    private sealed class RecordingBookmarkProvider : IBookmarkProvider
    {
        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct) => Task.FromResult(row);
        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetDueAsync(DateTime now, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingHistoryEventProvider : IHistoryEventProvider
    {
        public List<HistoryEventRow> Events { get; } = [];

        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyCollection<HistoryEventRow>>(Events);
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
}
