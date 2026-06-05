using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class RunStarterTests
{
    [Fact]
    public async Task Start_creates_run_counters_branch_and_history_in_correct_order()
    {
        // Arrange
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        var countersProvider = new RecordingRunCountersProvider(operations);
        var branchProvider = new RecordingBranchProvider(operations);
        var historyProvider = new RecordingHistoryEventProvider(operations);
        var workflowDefinitionProvider = new RecordingWorkflowDefinitionProvider();
        var starter = new RunStarter(
            runProvider,
            countersProvider,
            branchProvider,
            historyProvider,
            workflowDefinitionProvider,
            new CorrelationKeyResolver(),
            new FixedClock(),
            new SequenceIdGenerator());
        InboundEvent triggerEvent = new(
            "device",
            string.Empty,
            "evt-1",
            new Dictionary<string, JsonElement>
            {
                ["deviceSerial"] = JsonSerializer.SerializeToElement("serial-1"),
                ["payloadType"] = JsonSerializer.SerializeToElement("telemetry")
            },
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));

        // Act
        (long runId, long branchId) = await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), triggerEvent, CancellationToken.None);

        // Assert
        Assert.Equal(501L, runId);
        Assert.Equal(801L, branchId);
        Assert.Equal(["run", "counters", "branch", "history"], operations);
        Assert.Equal(1, countersProvider.TotalDelta);
        Assert.Equal("device:serial-1:telemetry", runProvider.CreatedRun!.CorrelationKey);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), runProvider.CreatedRun.RefId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), branchProvider.CreatedBranch!.RefId);
        Assert.Contains("\"trigger\"", branchProvider.CreatedBranch.LocalJson, StringComparison.Ordinal);
        Assert.Equal("RunStarted", historyProvider.Events.Single().EventKind);
    }

    private sealed class RecordingRunProvider(List<string> operations) : IRunProvider
    {
        public RunRow? CreatedRun { get; private set; }

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct)
        {
            operations.Add("run");
            CreatedRun = row with { Id = 501 };
            return Task.FromResult(CreatedRun);
        }

        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingRunCountersProvider(List<string> operations) : IRunCountersProvider
    {
        public int TotalDelta { get; private set; }

        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            operations.Add("counters");
            TotalDelta += delta;
            return Task.FromResult(TotalDelta);
        }

        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingBranchProvider(List<string> operations) : IBranchProvider
    {
        public BranchRow? CreatedBranch { get; private set; }

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
        {
            operations.Add("branch");
            CreatedBranch = row with { Id = 801 };
            return Task.FromResult(CreatedBranch);
        }

        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingHistoryEventProvider(List<string> operations) : IHistoryEventProvider
    {
        public List<HistoryEventRow> Events { get; } = [];

        public Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
        {
            operations.Add("history");
            Events.AddRange(events);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingWorkflowDefinitionProvider : IWorkflowDefinitionProvider
    {
        public Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();

        public Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct)
        {
            WorkflowDefinition definition = new(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                3,
                9,
                "definition",
                null,
                true,
                [new DeviceTriggerNode(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "device", [], new DeviceTriggerConfig("device-1", "telemetry"))],
                [],
                [],
                new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc),
                7);

            return Task.FromResult(new WorkflowDefinitionRow
            {
                Id = id,
                RefId = definition.WorkflowRefId,
                Version = definition.Version,
                WorkspaceId = definition.WorkspaceId,
                Name = definition.Name,
                Description = definition.Description,
                IsEnabled = definition.IsEnabled,
                DefinitionJson = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                PublishedBy = definition.PublishedBy,
                CreatedAt = definition.CreatedAt
            });
        }

        public Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task DeprecateAsync(int id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        private readonly Queue<Guid> _ids = new([
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222")]);

        public Guid NewId() => _ids.Dequeue();
    }
}






