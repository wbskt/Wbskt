using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class RunStarterTests
{
    [Fact]
    public async Task Start_creates_run_with_its_root_branch_then_history()
    {
        // Arrange
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        var historyProvider = new RecordingHistoryEventProvider(operations);
        var workflowDefinitionProvider = new RecordingWorkflowDefinitionProvider();
        var starter = new RunStarter(
            runProvider,
            new RunJournal(historyProvider, new FixedClock()),
            workflowDefinitionProvider,
            new CorrelationKeyResolver(TriggerChannelRegistry.Default),
            new NullRunStartedPublisher(),
            new FixedClock(),
            new SequenceIdGenerator(),
            Options.Create(new WorkflowEngineOptions()));
        InboundEvent triggerEvent = new(
            "client",
            [],
            "evt-1",
            new Dictionary<string, JsonElement>
            {
                ["clientRefId"] = JsonSerializer.SerializeToElement("serial-1"),
                ["messageType"] = JsonSerializer.SerializeToElement("telemetry")
            },
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));

        // Act
        (long runId, Guid runRefId, long branchId, bool created) = await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), triggerEvent, "trigger-start:5:abc", CancellationToken.None);

        // Assert
        Assert.Equal(501L, runId);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), runRefId);
        Assert.Equal(801L, branchId);
        Assert.True(created);
        Assert.Equal(["run", "history"], operations);
        Assert.Equal("trigger-start:5:abc", runProvider.StartKey);
        Assert.Equal("client:serial-1:telemetry", runProvider.CreatedRun!.CorrelationKey);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), runProvider.CreatedRun.RefId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), runProvider.RootBranch!.RefId);
        Assert.Equal(BranchStatus.Active, runProvider.RootBranch.Status);
        Assert.Null(runProvider.RootBranch.ParentBranchId);
        Assert.Contains("\"trigger\"", runProvider.RootBranch.LocalJson, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), historyProvider.Events.Single().BranchRefId);
        Assert.Equal("RunStarted", historyProvider.Events.Single().EventKind);
        Assert.Equal(new WorkflowEngineOptions().DefaultCreditBudgetPerRun, runProvider.CreatedRun.CreditBudget);

        // The arrival that caused the run is described on RunStarted: which channel, which key, when.
        // Without these the trace could not answer "what fired this?" - only "something did".
        string payload = historyProvider.Events.Single().PayloadJson!;
        Assert.Contains("\"channelKind\":\"client\"", payload, StringComparison.Ordinal);
        Assert.Contains("client:serial-1:telemetry", payload, StringComparison.Ordinal);
        Assert.Contains("\"receivedAt\"", payload, StringComparison.Ordinal);
        // The trigger body is not: it is caller-controlled, unbounded, and can carry credentials. It is
        // already persisted on the run, which is where to read it from.
        Assert.DoesNotContain("\"messageType\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_uses_configured_credit_budget_instead_of_default()
    {
        // Arrange
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        var starter = new RunStarter(
            runProvider,
            new RunJournal(new RecordingHistoryEventProvider(operations), new FixedClock()),
            new RecordingWorkflowDefinitionProvider(),
            new CorrelationKeyResolver(TriggerChannelRegistry.Default),
            new NullRunStartedPublisher(),
            new FixedClock(),
            new SequenceIdGenerator(),
            Options.Create(new WorkflowEngineOptions { DefaultCreditBudgetPerRun = 250m }));
        InboundEvent triggerEvent = new(
            "client",
            [],
            "evt-1",
            new Dictionary<string, JsonElement>
            {
                ["clientRefId"] = JsonSerializer.SerializeToElement("serial-1"),
                ["messageType"] = JsonSerializer.SerializeToElement("telemetry")
            },
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));

        // Act
        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), triggerEvent, null, CancellationToken.None);

        // Assert
        Assert.Equal(250m, runProvider.CreatedRun!.CreditBudget);
    }

    [Fact]
    public async Task Start_returns_the_run_already_started_under_the_key_without_announcing_it_again()
    {
        // A retried event whose earlier attempt created the run: the run is handed back, and no second
        // RunStarted lands in its history.
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations) { AlreadyStarted = true };
        var historyProvider = new RecordingHistoryEventProvider(operations);
        var starter = new RunStarter(
            runProvider,
            new RunJournal(historyProvider, new FixedClock()),
            new RecordingWorkflowDefinitionProvider(),
            new CorrelationKeyResolver(TriggerChannelRegistry.Default),
            new NullRunStartedPublisher(),
            new FixedClock(),
            new SequenceIdGenerator(),
            Options.Create(new WorkflowEngineOptions()));
        InboundEvent triggerEvent = new("client", [], "evt-1", new Dictionary<string, JsonElement>(), new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc))
        {
            CorrelationKey = "c"
        };

        RunStartResult result = await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), triggerEvent, "trigger-start:5:abc", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(400L, result.RunId);
        Assert.Equal(700L, result.BranchId);
        Assert.Equal(["run"], operations);
        Assert.Empty(historyProvider.Events);
    }

    [Fact]
    public async Task Start_passes_the_workspace_and_its_active_run_ceiling_to_the_store()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions { MaxActiveRunsPerWorkspace = 7 });

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None);

        Assert.Equal((await new RecordingWorkflowDefinitionProvider().GetByIdAsync(42, CancellationToken.None)).WorkspaceId, runProvider.WorkspaceId);
        Assert.Equal(7, runProvider.MaxActiveRuns);
    }

    [Fact]
    public async Task An_exempt_start_sends_no_ceiling_and_starts_in_a_full_workspace()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations) { AtCeiling = true };
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions { MaxActiveRunsPerWorkspace = 3 });

        RunStartResult result = await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None, exemptFromCeiling: true);

        Assert.True(result.Created);
        Assert.Null(runProvider.MaxActiveRuns);
    }

    [Fact]
    public async Task Start_sends_no_ceiling_when_it_is_switched_off()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions { MaxActiveRunsPerWorkspace = 0 });

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None);

        Assert.Null(runProvider.MaxActiveRuns);
    }

    [Fact]
    public async Task Start_at_the_ceiling_is_refused_and_writes_no_history()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations) { AtCeiling = true };
        var historyProvider = new RecordingHistoryEventProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions { MaxActiveRunsPerWorkspace = 3 }, historyProvider);

        var refused = await Assert.ThrowsAsync<WorkspaceRunCeilingException>(() =>
            starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), "trigger-start:5:abc", CancellationToken.None));

        Assert.Equal(3, refused.Limit);
        Assert.Equal(["run"], operations);
        Assert.Empty(historyProvider.Events);
    }

    private static RunStarter Starter(RecordingRunProvider runProvider, List<string> operations, WorkflowEngineOptions options, RecordingHistoryEventProvider? historyProvider = null) => new(
        runProvider,
        new RunJournal(historyProvider ?? new RecordingHistoryEventProvider(operations), new FixedClock()),
        new RecordingWorkflowDefinitionProvider(),
        new CorrelationKeyResolver(TriggerChannelRegistry.Default),
        new NullRunStartedPublisher(),
        new FixedClock(),
        new SequenceIdGenerator(),
        Options.Create(options));

    private static InboundEvent AnEvent() =>
        new("client", [], "evt-1", new Dictionary<string, JsonElement>(), new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)) { CorrelationKey = "c" };

    private sealed class RecordingRunProvider(List<string> operations) : IRunProvider
    {
        public RunRow? CreatedRun { get; private set; }
        public BranchRow? RootBranch { get; private set; }
        public string? StartKey { get; private set; }
        public bool AlreadyStarted { get; init; }
        public bool AtCeiling { get; init; }
        public int WorkspaceId { get; private set; }
        public int? MaxActiveRuns { get; private set; }

        public Task<RunStartRow> StartAsync(RunRow run, BranchRow rootBranch, string? triggerStartKey, int workspaceId, int? maxActiveRuns, CancellationToken ct)
        {
            WorkspaceId = workspaceId;
            MaxActiveRuns = maxActiveRuns;
            if (AtCeiling && maxActiveRuns is not null)
            {
                operations.Add("run");
                throw AuthHost.SqlExceptions.WithNumber(50026, "The workspace has reached its limit of active runs.");
            }

            return StartAsync(run, rootBranch, triggerStartKey, ct);
        }

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();

        public Task<RunStartRow> StartAsync(RunRow run, BranchRow rootBranch, string? triggerStartKey, CancellationToken ct)
        {
            operations.Add("run");
            StartKey = triggerStartKey;
            if (AlreadyStarted)
            {
                return Task.FromResult(new RunStartRow(run with { Id = 400 }, 700, false));
            }

            CreatedRun = run with { Id = 501 };
            RootBranch = rootBranch;
            return Task.FromResult(new RunStartRow(CreatedRun, 801, true));
        }

        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow?> FindRowByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
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

        public Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, DateTime? elevatedCutoffUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingWorkflowDefinitionProvider : IWorkflowDefinitionProvider
    {
        public Task<IReadOnlyCollection<WorkflowVersionRow>> GetVersionsAsync(Guid refId, int workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDeletion?> DeleteAsync(Guid refId, int workspaceId, int deletedBy, CancellationToken ct) => throw new NotSupportedException();
        public Task<Wbskt.Models.IPagedList<WorkflowDefinitionRow>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct) => Task.FromResult<Wbskt.Models.IPagedList<WorkflowDefinitionRow>>(new Wbskt.Models.PagedList<WorkflowDefinitionRow>(Array.Empty<WorkflowDefinitionRow>(), 0));
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
                [new ClientTriggerNode { NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Name = "client", Ports = [], Config = new ClientTriggerConfig { ClientRef = "client-1", Type = "telemetry" } }],
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

        public Task<WorkflowDefinitionRow?> FindRowByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow?> FindCurrentByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, IReadOnlyCollection<Wbskt.Workflow.Abstraction.Integrations.WorkflowIntegrationRef> integrationRefs, CancellationToken ct) => InsertAsync(row, ct);

        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task DeprecateAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> DeleteUnreferencedAsync(int id, CancellationToken ct) => Task.FromResult(true);
        public Task SetEnabledAsync(int id, bool isEnabled, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;

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






