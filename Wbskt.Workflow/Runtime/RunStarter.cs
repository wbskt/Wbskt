using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Telemetry;
using Microsoft.Extensions.Logging;

namespace Wbskt.Workflow.Runtime;

internal sealed class RunStarter : IRunStarter
{
    private const int WorkspaceRunCeilingError = 50026; // THROW in dbo.Run_Start

    private readonly IRunProvider _runProvider;
    private readonly IRunJournal _journal;
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ICorrelationKeyResolver _correlationKeyResolver;
    private readonly IRunStartedPublisher _runStartedPublisher;
    private readonly TimeProvider _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly decimal _defaultCreditBudgetPerRun;
    private readonly int? _maxActiveRunsPerWorkspace;
    private readonly WorkflowMetrics? _workflowMetrics;
    private readonly ILogger<RunStarter>? _logger;

    public RunStarter(
        IRunProvider runProvider,
        IRunJournal journal,
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ICorrelationKeyResolver correlationKeyResolver,
        IRunStartedPublisher runStartedPublisher,
        TimeProvider clock,
        IIdGenerator idGenerator,
        IOptions<WorkflowEngineOptions> options,
        WorkflowMetrics? workflowMetrics = null,
        ILogger<RunStarter>? logger = null)
    {
        _runProvider = runProvider;
        _journal = journal;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _correlationKeyResolver = correlationKeyResolver;
        _runStartedPublisher = runStartedPublisher;
        _clock = clock;
        _idGenerator = idGenerator;
        _defaultCreditBudgetPerRun = options.Value.DefaultCreditBudgetPerRun;
        _maxActiveRunsPerWorkspace = options.Value.MaxActiveRunsPerWorkspace > 0 ? options.Value.MaxActiveRunsPerWorkspace : null;
        _workflowMetrics = workflowMetrics;
        _logger = logger;
    }

    public async Task<RunStartResult> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, string? triggerStartKey, CancellationToken ct)
    {
        try
        {
            WorkflowDefinitionRow definition = await _workflowDefinitionProvider.GetByIdAsync(workflowDefinitionId, ct);
            decimal creditBudget = ResolveCreditBudget(definition.DefinitionJson);
            DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

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
                started = await _runProvider.StartAsync(
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
                        CreatedAt = nowUtc
                    },
                    new BranchRow
                    {
                        Id = 0,
                        RefId = rootBranchRefId,
                        RunId = 0,
                        ParentBranchId = null,
                        ForkCohortId = null,
                        NodeId = Guid.Parse(triggerNodeId),
                        Status = BranchStatus.Active,
                        PendingTakePort = null,
                        LocalJson = localJson,
                        LastOutputJson = null,
                        CompensationStackJson = null,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc,
                        // Branches.RowVersion is a SQL Server ROWVERSION: the database assigns it.
                        RowVersion = Array.Empty<byte>()
                    },
                    triggerStartKey,
                    definition.WorkspaceId,
                    _maxActiveRunsPerWorkspace,
                    ct);
            }
            catch (SqlException ex) when (ex.Number == WorkspaceRunCeilingError)
            {
                _workflowMetrics?.RecordRunRefused(definition.RefId.ToString(), triggerEvent.ChannelKind, definition.WorkspaceId);
                _logger?.LogWarning("Workspace {WorkspaceId} is at its limit of {Limit} active runs; workflow {WorkflowRefId} was not started for {Channel}",
                    definition.WorkspaceId, _maxActiveRunsPerWorkspace, definition.RefId, triggerEvent.ChannelKind);
                throw new WorkspaceRunCeilingException(definition.WorkspaceId, _maxActiveRunsPerWorkspace!.Value);
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

            _workflowMetrics?.RecordRunStarted(definition.RefId.ToString(), triggerEvent.ChannelKind, definition.WorkspaceId);
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
        catch (Exception ex) when (ex is not WorkspaceRunCeilingException)
        {
            _logger?.LogError(ex, "Failed to start run for workflow definition {WorkflowDefinitionId} and trigger {TriggerNodeId}", workflowDefinitionId, triggerNodeId);
            throw;
        }
    }

    public async Task<RunStartResult?> FindStartedAsync(string triggerStartKey, CancellationToken ct)
    {
        RunStartRow? started = await _runProvider.FindByTriggerStartKeyAsync(triggerStartKey, ct);
        return started is null ? null : new RunStartResult(started.Run.Id, started.Run.RefId, started.RootBranchId, false);
    }

    // Reads the optional per-workflow "creditBudget" from the definition JSON (a cheap property peek,
    // not a full node deserialize), falling back to the configured default when unset or non-positive.
    private decimal ResolveCreditBudget(string definitionJson)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(definitionJson);
            if (doc.RootElement.TryGetProperty("creditBudget", out JsonElement creditBudget)
                && creditBudget.ValueKind == JsonValueKind.Number
                && creditBudget.TryGetDecimal(out decimal budget)
                && budget > 0)
            {
                return budget;
            }
        }
        catch (JsonException)
        {
            // A malformed definition would already fail the branch loop; fall back to the default here.
        }

        return _defaultCreditBudgetPerRun;
    }
}
