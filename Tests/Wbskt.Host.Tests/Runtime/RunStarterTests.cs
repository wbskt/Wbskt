using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Host.Tests.Runtime;

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
            new WorkflowDefinitionCache(new MemoryCache(new MemoryCacheOptions()), workflowDefinitionProvider),
            new CorrelationKeyResolver(TriggerChannelRegistry.Default),
            new NullRunStartedPublisher(),
            new FixedClock(),
            new SequenceIdGenerator(),
            new FixedWorkspacePlans(),
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
            CacheOver(new RecordingWorkflowDefinitionProvider()),
            new CorrelationKeyResolver(TriggerChannelRegistry.Default),
            new NullRunStartedPublisher(),
            new FixedClock(),
            new SequenceIdGenerator(),
            new FixedWorkspacePlans(),
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
            CacheOver(new RecordingWorkflowDefinitionProvider()),
            new CorrelationKeyResolver(TriggerChannelRegistry.Default),
            new NullRunStartedPublisher(),
            new FixedClock(),
            new SequenceIdGenerator(),
            new FixedWorkspacePlans(),
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
    public async Task Start_passes_the_workspace_and_its_plans_active_run_ceiling_to_the_store()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        var plans = new FixedWorkspacePlans(PlanCode.Maker, tenantId: 9, adjust: l => l with { ActiveRuns = 7 });
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions(), plans: plans);

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None);

        Assert.Equal((await new RecordingWorkflowDefinitionProvider().GetByIdAsync(42, CancellationToken.None)).WorkspaceId, runProvider.WorkspaceId);
        Assert.Equal(new PlanCeiling(7, 9), runProvider.Ceiling);
    }

    [Fact]
    public async Task A_workspace_whose_account_has_not_arrived_is_held_to_free_on_its_own()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions());

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None);

        Assert.Equal(new PlanCeiling(PlanCatalog.BuiltIn[PlanCode.Free].ActiveRuns, null), runProvider.Ceiling);
    }

    [Fact]
    public async Task An_exempt_start_sends_no_ceiling_and_starts_in_a_full_workspace()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations) { AtCeiling = true };
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions());

        RunStartResult result = await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None, exemptFromCeiling: true);

        Assert.True(result.Created);
        Assert.Null(runProvider.Ceiling);
    }

    [Fact]
    public async Task Start_passes_the_plans_monthly_credits_to_the_store()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        var plans = new FixedWorkspacePlans(PlanCode.Maker, tenantId: 9, adjust: l => l with { CreditsPerMonth = 123m });
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions(), plans: plans);

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None);

        Assert.Equal(new PlanCredits(123m, 9), runProvider.Credits);
    }

    [Fact]
    public async Task Start_once_the_months_credits_are_spent_is_refused_and_writes_no_history()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations) { CreditsSpent = true };
        var historyProvider = new RecordingHistoryEventProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions(), historyProvider,
            plans: new FixedWorkspacePlans(adjust: l => l with { CreditsPerMonth = 50m }));

        var refused = await Assert.ThrowsAsync<PlanCreditLimitException>(() =>
            starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), "trigger-start:5:abc", CancellationToken.None));

        Assert.Equal(50m, refused.Allowance);
        Assert.Equal(["run"], operations);
        Assert.Empty(historyProvider.Events);
    }

    [Fact]
    public async Task Start_once_the_workspaces_cap_is_spent_is_refused_as_such()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations) { CapSpent = true };
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions());

        var refused = await Assert.ThrowsAsync<WorkspaceCreditCapException>(() =>
            starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None));

        Assert.Equal((await new RecordingWorkflowDefinitionProvider().GetByIdAsync(42, CancellationToken.None)).WorkspaceId, refused.WorkspaceId);
        Assert.Equal(["run"], operations);
    }

    [Fact]
    public async Task An_exempt_start_sends_no_credit_allowance()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations) { CreditsSpent = true };
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions());

        RunStartResult result = await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None, exemptFromCeiling: true);

        Assert.True(result.Created);
        Assert.Null(runProvider.Credits);
    }

    [Fact]
    public async Task Start_at_the_ceiling_is_refused_and_writes_no_history()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations) { AtCeiling = true };
        var historyProvider = new RecordingHistoryEventProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions(), historyProvider,
            plans: new FixedWorkspacePlans(adjust: l => l with { ActiveRuns = 3 }));

        var refused = await Assert.ThrowsAsync<PlanRunLimitException>(() =>
            starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), "trigger-start:5:abc", CancellationToken.None));

        Assert.Equal(3, refused.Limit);
        Assert.Equal(["run"], operations);
        Assert.Empty(historyProvider.Events);
    }

    [Theory]
    [InlineData(75, 75)]
    [InlineData(0, 100)]
    public async Task Start_takes_the_credit_budget_from_the_cached_definition(int definitionBudget, int expected)
    {
        // The row's JSON carries no budget at all, so a budget on the run can only have come from the
        // cached definition's property. A non-positive one falls back to the configured default.
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        WorkflowDefinition cached = (await CacheOver(new RecordingWorkflowDefinitionProvider()).GetAsync(42, CancellationToken.None)) with { CreditBudget = definitionBudget };
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions { DefaultCreditBudgetPerRun = 100m }, definitionCache: new FixedDefinitionCache(cached));

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None);

        Assert.Equal((decimal)expected, runProvider.CreatedRun!.CreditBudget);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Start_records_the_parent_a_sub_workflow_step_named_and_only_for_such_a_start(bool startsChildRun, bool recorded)
    {
        // A manual start from the API can put anything in its body; a run that claimed another's run as
        // its parent would be cancelled with it. Only the step's own start is believed.
        Guid parent = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions());
        InboundEvent evt = new InboundEvent(
            "manual",
            [],
            "evt-child",
            new Dictionary<string, JsonElement> { ["body"] = JsonSerializer.SerializeToElement(new { parentRunRefId = parent, depth = 1 }) },
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)) { CorrelationKey = "c", StartsChildRun = startsChildRun };

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), evt, null, CancellationToken.None);

        Assert.Equal(recorded ? parent : null, runProvider.CreatedRun!.ParentRunRefId);
    }

    [Fact]
    public async Task Start_of_a_run_with_no_parent_records_none()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions());

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent() with { StartsChildRun = true }, null, CancellationToken.None);

        Assert.Null(runProvider.CreatedRun!.ParentRunRefId);
    }

    [Fact]
    public async Task Start_charges_the_run_start_cost_with_the_run()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions());

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), null, CancellationToken.None);

        Assert.Equal(new CreditCostOptions().RunStartCost, runProvider.CreatedRun!.StartCost);
        Assert.Equal(1m, runProvider.CreatedRun.StartCost);
    }

    [Fact]
    public async Task Start_charges_the_configured_run_start_cost()
    {
        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations);
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions(), creditCosts: new CreditCostOptions { RunStartCost = 2.5m });

        await starter.StartTestAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), CancellationToken.None);

        Assert.Equal(2.5m, runProvider.CreatedRun!.StartCost);
        Assert.True(runProvider.CreatedRun.IsTest);
    }

    [Theory]
    [InlineData(false, 1.0)]
    [InlineData(true, 0.0)]
    public async Task Start_counts_the_run_start_charge_as_consumed_credits_only_when_it_creates_the_run(bool alreadyStarted, double expected)
    {
        using var metrics = new WorkflowMetrics();
        double consumed = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (ReferenceEquals(instrument, metrics.CreditsConsumed))
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, measurement, _, _) => consumed += measurement);
        listener.Start();

        List<string> operations = [];
        var runProvider = new RecordingRunProvider(operations) { AlreadyStarted = alreadyStarted };
        RunStarter starter = Starter(runProvider, operations, new WorkflowEngineOptions(), workflowMetrics: metrics);

        await starter.StartAsync(42, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(), AnEvent(), "key-1", CancellationToken.None);

        Assert.Equal(expected, consumed);
    }

    private static RunStarter Starter(RecordingRunProvider runProvider, List<string> operations, WorkflowEngineOptions options, RecordingHistoryEventProvider? historyProvider = null, IWorkflowDefinitionCache? definitionCache = null, CreditCostOptions? creditCosts = null, WorkflowMetrics? workflowMetrics = null, IWorkspacePlans? plans = null) => new(
        runProvider,
        new RunJournal(historyProvider ?? new RecordingHistoryEventProvider(operations), new FixedClock()),
        new RecordingWorkflowDefinitionProvider(),
        definitionCache ?? CacheOver(new RecordingWorkflowDefinitionProvider()),
        new CorrelationKeyResolver(TriggerChannelRegistry.Default),
        new NullRunStartedPublisher(),
        new FixedClock(),
        new SequenceIdGenerator(),
        plans ?? new FixedWorkspacePlans(),
        Options.Create(options),
        workflowMetrics,
        creditCosts: creditCosts is null ? null : Options.Create(creditCosts));

    private static WorkflowDefinitionCache CacheOver(IWorkflowDefinitionProvider provider) =>
        new(new MemoryCache(new MemoryCacheOptions()), provider);

    private sealed class FixedDefinitionCache(WorkflowDefinition definition) : IWorkflowDefinitionCache
    {
        public Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct) => Task.FromResult(definition);

        public void Invalidate(int workflowDefinitionId)
        {
        }
    }

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
        public PlanCeiling? Ceiling { get; private set; }
        public bool CreditsSpent { get; init; }
        public bool CapSpent { get; init; }
        public PlanCredits? Credits { get; private set; }

        public Task<RunStartRow> StartAsync(RunRow run, BranchRow rootBranch, string? triggerStartKey, int workspaceId, PlanCeiling? ceiling, ActiveRunGuard activeRunGuard, PlanCredits? creditAllowance, CancellationToken ct)
        {
            Credits = creditAllowance;
            if (CreditsSpent && creditAllowance is not null)
            {
                operations.Add("run");
                throw AuthHost.SqlExceptions.WithNumber(50033, "The plan has used its workflow credits for this month.");
            }

            if (CapSpent && creditAllowance is not null)
            {
                operations.Add("run");
                throw AuthHost.SqlExceptions.WithNumber(50034, "The workspace has used its monthly credit cap.");
            }

            return StartAsync(run, rootBranch, triggerStartKey, workspaceId, ceiling, ct);
        }

        public Task<RunStartRow> StartAsync(RunRow run, BranchRow rootBranch, string? triggerStartKey, int workspaceId, PlanCeiling? ceiling, CancellationToken ct)
        {
            WorkspaceId = workspaceId;
            Ceiling = ceiling;
            if (AtCeiling && ceiling is not null)
            {
                operations.Add("run");
                throw AuthHost.SqlExceptions.WithNumber(50026, "The plan has reached its limit of active runs.");
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






