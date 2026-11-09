using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Telemetry;
using Microsoft.Extensions.Logging;

namespace Wbskt.Workflow.Runtime;

internal sealed class RunStarter : IRunStarter
{
    private const int PlanRunLimitError = 50026; // THROW in dbo.Run_Start
    private const int ActiveRunConflictError = 50028; // THROW in dbo.Run_Start
    private const int TestRunActiveError = 50029; // THROW in dbo.Run_Start
    private const int PlanCreditLimitError = 50033; // THROW in dbo.Run_Start
    private const int WorkspaceCreditCapError = 50034; // THROW in dbo.Run_Start
    private const int StartBusyError = 50035; // THROW in dbo.Run_Start

    // Tries of a start that times out waiting for its tenant's or workspace's lock, and the pause after
    // each but the last: contention clears in well under a second unless a burst keeps it held.
    internal static readonly TimeSpan[] BusyBackoff = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200)];

    private readonly IRunProvider _runProvider;
    private readonly IRunJournal _journal;
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly IWorkflowDefinitionCache _workflowDefinitionCache;
    private readonly ICorrelationKeyResolver _correlationKeyResolver;
    private readonly IRunStartedPublisher _runStartedPublisher;
    private readonly TimeProvider _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly decimal _defaultCreditBudgetPerRun;
    private readonly decimal _runStartCost;
    private readonly IWorkspacePlans _plans;
    private readonly WorkflowMetrics? _workflowMetrics;
    private readonly ILogger<RunStarter>? _logger;

    public RunStarter(
        IRunProvider runProvider,
        IRunJournal journal,
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        IWorkflowDefinitionCache workflowDefinitionCache,
        ICorrelationKeyResolver correlationKeyResolver,
        IRunStartedPublisher runStartedPublisher,
        TimeProvider clock,
        IIdGenerator idGenerator,
        IWorkspacePlans plans,
        IOptions<WorkflowEngineOptions> options,
        WorkflowMetrics? workflowMetrics = null,
        ILogger<RunStarter>? logger = null,
        IOptions<CreditCostOptions>? creditCosts = null)
    {
        _runProvider = runProvider;
        _journal = journal;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _workflowDefinitionCache = workflowDefinitionCache;
        _correlationKeyResolver = correlationKeyResolver;
        _runStartedPublisher = runStartedPublisher;
        _clock = clock;
        _idGenerator = idGenerator;
        _defaultCreditBudgetPerRun = options.Value.DefaultCreditBudgetPerRun;
        _runStartCost = (creditCosts?.Value ?? new CreditCostOptions()).RunStartCost;
        _plans = plans;
        _workflowMetrics = workflowMetrics;
        _logger = logger;
    }

    public Task<RunStartResult> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, string? triggerStartKey, CancellationToken ct, bool exemptFromCeiling = false, ActiveRunGuard activeRunGuard = ActiveRunGuard.None) =>
        StartCoreAsync(workflowDefinitionId, triggerNodeId, triggerEvent, triggerStartKey, exemptFromCeiling, activeRunGuard, isTest: false, ct);

    public Task<RunStartResult> StartTestAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, CancellationToken ct) =>
        StartCoreAsync(workflowDefinitionId, triggerNodeId, triggerEvent, triggerStartKey: null, exemptFromCeiling: false, ActiveRunGuard.None, isTest: true, ct);

    private async Task<RunStartResult> StartCoreAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, string? triggerStartKey, bool exemptFromCeiling, ActiveRunGuard activeRunGuard, bool isTest, CancellationToken ct)
    {
        try
        {
            WorkflowDefinitionRow definition = await _workflowDefinitionProvider.GetByIdAsync(workflowDefinitionId, ct);
            decimal creditBudget = ResolveCreditBudget(await _workflowDefinitionCache.GetAsync(workflowDefinitionId, ct));
            DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
            WorkspacePlan plan = await _plans.GetAsync(definition.WorkspaceId, ct);
            PlanCeiling? ceiling = exemptFromCeiling ? null : plan.ActiveRunCeiling;
            Guid? parentRunRefId = ReadParentRunRefId(triggerEvent);

            // A sub-workflow child spends from its parent's budget (Run_Start), so only it skips the
            // month's credits and the workspace cap; a replay or a replacement run is new work.
            PlanCredits? credits = parentRunRefId is null ? plan.Credits : null;

            // In practice the dispatcher has always set this by now - it normalizes the event per
            // registration before starting a run. The fallback is kept because this is a public entry point
            // on the runtime: a caller reaching it another way would otherwise persist a null correlation
            // key, and every concurrency policy is keyed on that.
            string correlationKey = triggerEvent.CorrelationKey ?? _correlationKeyResolver.Resolve(triggerEvent);

            // Generated outside the DB so tests can pin them.
            Guid runRefId = _idGenerator.NewId();
            Guid rootBranchRefId = _idGenerator.NewId();
            string localJson = JsonSerializer.Serialize(
                new Dictionary<string, JsonElement>
                {
                    ["trigger"] = JsonSerializer.SerializeToElement(triggerEvent.Payload)
                },
                EngineJson.Options);

            // The run, its counters (seeded with this one branch, mirroring the Fork path, which counts
            // the child branches it creates) and the root branch are written in one transaction, so a
            // failure part-way leaves either all of them or none. That is what lets a retry that finds
            // the run under its trigger start key take it as fully started.
            RunStartRow started;
            try
            {
                started = await StartWithRetryAsync(definition.WorkspaceId, () => _runProvider.StartAsync(
                    new RunRow
                    {
                        Id = 0,
                        RefId = runRefId,
                        WorkflowDefinitionId = workflowDefinitionId,
                        WorkflowRefId = definition.RefId,
                        WorkflowVersion = definition.Version,
                        TriggerNodeId = Guid.Parse(triggerNodeId),
                        CorrelationKey = correlationKey,
                        Status = RunStatus.Running,
                        StartedAt = nowUtc,
                        CompletedAt = null,
                        CancellationRequestedAt = null,
                        CancellationReason = null,
                        CreditBudget = creditBudget,
                        CreatedAt = nowUtc,
                        ParentRunRefId = parentRunRefId,
                        IsTest = isTest,
                        StartCost = _runStartCost
                    },
                    // RunId 0: Run_Start inserts the root branch under the run it has just created.
                    BranchRow.New(rootBranchRefId, 0, Guid.Parse(triggerNodeId), localJson, nowUtc),
                    triggerStartKey,
                    definition.WorkspaceId,
                    ceiling,
                    activeRunGuard,
                    credits,
                    ct), ct);
            }
            catch (SqlException ex) when (ex.Number == ActiveRunConflictError)
            {
                throw new ActiveRunConflictException(correlationKey);
            }
            catch (SqlException ex) when (ex.Number == TestRunActiveError)
            {
                throw new TestRunActiveException(definition.RefId);
            }
            catch (SqlException ex) when (ex.Number == PlanRunLimitError)
            {
                _workflowMetrics?.RecordRunRefused(definition.RefId.ToString(), triggerEvent.ChannelKind, definition.WorkspaceId);
                _logger?.LogWarning("Workspace {WorkspaceId} is at its plan's limit of {Limit} active runs; workflow {WorkflowRefId} was not started for {Channel}",
                    definition.WorkspaceId, ceiling!.Limit, definition.RefId, triggerEvent.ChannelKind);
                throw new PlanRunLimitException(definition.WorkspaceId, ceiling.Limit);
            }
            catch (SqlException ex) when (ex.Number == PlanCreditLimitError)
            {
                _workflowMetrics?.RecordRunRefused(definition.RefId.ToString(), triggerEvent.ChannelKind, definition.WorkspaceId);
                _logger?.LogWarning("Workspace {WorkspaceId} has spent its plan's {Allowance} credits for the month; workflow {WorkflowRefId} was not started for {Channel}",
                    definition.WorkspaceId, credits!.MonthlyAllowance, definition.RefId, triggerEvent.ChannelKind);
                throw new PlanCreditLimitException(definition.WorkspaceId, credits.MonthlyAllowance);
            }
            catch (SqlException ex) when (ex.Number == WorkspaceCreditCapError)
            {
                _workflowMetrics?.RecordRunRefused(definition.RefId.ToString(), triggerEvent.ChannelKind, definition.WorkspaceId);
                _logger?.LogWarning("Workspace {WorkspaceId} has spent its monthly credit cap; workflow {WorkflowRefId} was not started for {Channel}",
                    definition.WorkspaceId, definition.RefId, triggerEvent.ChannelKind);
                throw new WorkspaceCreditCapException(definition.WorkspaceId);
            }

            RunRow createdRun = started.Run;
            if (!started.Created)
            {
                // An earlier attempt at this event created the run and failed before it was recorded as
                // started. Its journal entry and announcement may or may not have gone out; a run with
                // one missing is better than a second run.
                _logger?.LogInformation("Run {RunId} already started under {TriggerStartKey}; recovering it", createdRun.Id, triggerStartKey);
                return new RunStartResult(createdRun.Id, createdRun.RefId, started.RootBranchId, false);
            }

            _workflowMetrics?.RecordRunStarted(definition.RefId.ToString(), triggerEvent.ChannelKind, definition.WorkspaceId, createdRun.IsTest);

            // The start charge went into the counters with the run, so it is counted here and not again
            // on a recovered start above, which charged nothing new.
            if (_runStartCost > 0)
            {
                _workflowMetrics?.RecordCreditsConsumed(definition.RefId.ToString(), (double)_runStartCost, definition.WorkspaceId);
            }

            await _journal.AppendAsync(
                new RunJournalEntry(createdRun.Id, HistoryEventKind.RunStarted)
                {
                    BranchRefId = rootBranchRefId,
                    NodeId = Guid.Parse(triggerNodeId),
                    // Describes the arrival that caused this run: which channel it came in on, which key it
                    // matched and when it was received. That is the inbound record the trace was missing;
                    // a separate InboundEventReceived row would say the same thing one line earlier.
                    //
                    // The trigger body itself is deliberately NOT recorded. It is caller-controlled and
                    // unbounded - the public callback caps a request body, not the history table - and it can
                    // carry anything the caller sends, including credentials. The run's trigger payload is
                    // already persisted on the run and is where to read it from.
                    Payload = new
                    {
                        triggerEvent.InboundEventId,
                        CorrelationKey = correlationKey,
                        triggerEvent.ChannelKind,
                        MatchKeys = triggerEvent.MatchKeys,
                        triggerEvent.ReceivedAt
                    }
                },
                ct);

            // Wrapped rather than calling the bus directly so Wbskt.Workflow carries no event-bus
            // dependency: the engine host supplies a publisher that forwards to MassTransit, and the
            // management host and every unit test get a no-op without needing a broker.
            await _runStartedPublisher.PublishAsync(createdRun, ct);

            return new RunStartResult(createdRun.Id, createdRun.RefId, started.RootBranchId, true);
        }
        catch (Exception ex) when (ex is not PlanRunLimitException and not PlanCreditLimitException and not WorkspaceCreditCapException and not ActiveRunConflictException and not TestRunActiveException and not RunStartBusyException)
        {
            _logger?.LogError(ex, "Failed to start run for workflow definition {WorkflowDefinitionId} and trigger {TriggerNodeId}", workflowDefinitionId, triggerNodeId);
            throw;
        }
    }

    /// <summary>
    /// Runs the start, trying again after a pause when it timed out waiting for its tenant's or
    /// workspace's lock; after the last try it throws <see cref="RunStartBusyException"/>. A lock timeout
    /// says other starts are holding the lock, not that the plan is full.
    /// </summary>
    private async Task<RunStartRow> StartWithRetryAsync(int workspaceId, Func<Task<RunStartRow>> start, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await start();
            }
            catch (SqlException ex) when (ex.Number == StartBusyError)
            {
                bool gaveUp = attempt >= BusyBackoff.Length;
                _workflowMetrics?.RecordRunStartBusy(workspaceId, gaveUp);
                if (gaveUp)
                {
                    _logger?.LogWarning("Workspace {WorkspaceId}: gave up starting a run after {Attempts} tries waiting for its plan lock", workspaceId, attempt + 1);
                    throw new RunStartBusyException(workspaceId);
                }

                await Task.Delay(BusyBackoff[attempt], _clock, ct);
            }
        }
    }

    public async Task<RunStartResult?> FindStartedAsync(string triggerStartKey, CancellationToken ct)
    {
        RunStartRow? started = await _runProvider.FindByTriggerStartKeyAsync(triggerStartKey, ct);
        return started is null ? null : new RunStartResult(started.Run.Id, started.Run.RefId, started.RootBranchId, false);
    }

    /// <summary>
    /// The parent a sub-workflow step named in the child's body, so cancelling or failing the parent can
    /// find the child. Read only for a start the step itself made: a manual start from the API can put
    /// anything in its body, and a run that claimed another's run as its parent would be cancelled with it.
    /// </summary>
    private static Guid? ReadParentRunRefId(InboundEvent triggerEvent)
    {
        if (triggerEvent.StartsChildRun
            && triggerEvent.Payload.TryGetValue("body", out JsonElement body)
            && body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(SubWorkflowConfig.ParentRunRefIdKey, out JsonElement parent)
            && parent.ValueKind == JsonValueKind.String
            && parent.TryGetGuid(out Guid parentRunRefId))
        {
            return parentRunRefId;
        }

        return null;
    }

    // The optional per-workflow budget, falling back to the configured default when unset or
    // non-positive. Read from the cached definition rather than by parsing the row's JSON again: the
    // branch loop needs the deserialized definition for this run anyway, so the cache already holds it.
    private decimal ResolveCreditBudget(WorkflowDefinition definition) =>
        definition.CreditBudget is { } budget && budget > 0 ? budget : _defaultCreditBudgetPerRun;
}
