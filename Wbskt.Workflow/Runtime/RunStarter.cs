using System.Text.Json;
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
    private readonly IRunProvider _runProvider;
    private readonly IRunCountersProvider _runCountersProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IHistoryEventProvider _historyEventProvider;
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ICorrelationKeyResolver _correlationKeyResolver;
    private readonly IRunStartedPublisher _runStartedPublisher;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly decimal _defaultCreditBudgetPerRun;
    private readonly WorkflowMetrics? _workflowMetrics;
    private readonly ILogger<RunStarter>? _logger;

    public RunStarter(
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IBranchProvider branchProvider,
        IHistoryEventProvider historyEventProvider,
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ICorrelationKeyResolver correlationKeyResolver,
        IRunStartedPublisher runStartedPublisher,
        IClock clock,
        IIdGenerator idGenerator,
        IOptions<WorkflowEngineOptions>? options = null,
        WorkflowMetrics? workflowMetrics = null,
        ILogger<RunStarter>? logger = null)
    {
        _runProvider = runProvider;
        _runCountersProvider = runCountersProvider;
        _branchProvider = branchProvider;
        _historyEventProvider = historyEventProvider;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _correlationKeyResolver = correlationKeyResolver;
        _runStartedPublisher = runStartedPublisher;
        _clock = clock;
        _idGenerator = idGenerator;
        _defaultCreditBudgetPerRun = options?.Value.DefaultCreditBudgetPerRun ?? new WorkflowEngineOptions().DefaultCreditBudgetPerRun;
        _workflowMetrics = workflowMetrics;
        _logger = logger;
    }

    public async Task<(long RunId, long BranchId)> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, CancellationToken ct)
    {
        try
        {
            WorkflowDefinitionRow definition = await _workflowDefinitionProvider.GetByIdAsync(workflowDefinitionId, ct);
            _workflowMetrics?.RecordRunStarted(definition.RefId.ToString(), triggerEvent.ChannelKind, definition.WorkspaceId);
            decimal creditBudget = ResolveCreditBudget(definition.DefinitionJson);
            DateTime nowUtc = _clock.UtcNow;
        // In practice the dispatcher has always set this by now - it normalizes the event per
        // registration before starting a run. The fallback is kept because this is a public entry point
        // on the runtime: a caller reaching it another way would otherwise persist a null correlation
        // key, and every concurrency policy is keyed on that.
        string correlationKey = triggerEvent.CorrelationKey ?? _correlationKeyResolver.Resolve(triggerEvent);

        RunRow createdRun = await _runProvider.CreateAsync(new RunRow
        {
            Id = 0,
            RefId = _idGenerator.NewId(), // generated outside the DB so tests can pin it
            WorkflowDefinitionId = workflowDefinitionId,
            WorkflowRefId = definition.RefId,
            WorkflowVersion = definition.Version,
            TriggerNodeId = Guid.Parse(triggerNodeId),
            CorrelationKey = correlationKey,
            Status = "Running",
            StartedAt = nowUtc,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = creditBudget,
            CreatedAt = nowUtc
        }, ct);

        // Account for the initial branch in the run's active-branch counter so the
        // run can finalize when this branch completes (mirrors the Fork path, which
        // counts the child branches it creates). Run_Create seeds the counter at 0.
        
        // Safe to increment by id even though nothing wrote a counter row here: dbo.Run_Create seeds
        // dbo.RunCounters as part of creating the run.
        await _runCountersProvider.IncrementActiveBranchesAsync(createdRun.Id, 1, ct);

        string localJson = JsonSerializer.Serialize(
            new Dictionary<string, JsonElement>
            {
                ["trigger"] = JsonSerializer.SerializeToElement(triggerEvent.Payload)
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        BranchRow createdBranch = await _branchProvider.CreateAsync(new BranchRow
        {
            Id = 0,
            RefId = _idGenerator.NewId(),
            RunId = createdRun.Id,
            ParentBranchId = null,
            ForkCohortId = null,
            NodeId = Guid.Parse(triggerNodeId),
            Status = "Active",
            PendingTakePort = null,
            LocalJson = localJson,
            LastOutputJson = null,
            CompensationStackJson = null,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            // Branches.RowVersion is a SQL Server ROWVERSION: the database assigns it, so nothing sent on
            // insert is used. It is mapped on read for completeness, but Branch_Update does not yet
            // compare it, so the engine has no optimistic-concurrency check on a branch row. That is
            // survivable because a branch is only ever driven by one host at a time (the pump claims it),
            // and it is the obvious hook if that ever stops being true.
            RowVersion = Array.Empty<byte>()
        }, ct);

        await _historyEventProvider.InsertBatchAsync([
            new HistoryEventRow
            {
                HistoryEventId = 0,
                RunId = createdRun.Id,
                BranchRefId = createdBranch.RefId,
                NodeId = Guid.Parse(triggerNodeId),
                EventKind = HistoryEventKind.RunStarted,
                Severity = HistoryEventKind.SeverityFor(HistoryEventKind.RunStarted),
                // Describes the arrival that caused this run: which channel it came in on, which key it
                // matched and when it was received. That is the inbound record the trace was missing;
                // a separate InboundEventReceived row would say the same thing one line earlier.
                //
                // The trigger body itself is deliberately NOT recorded. It is caller-controlled and
                // unbounded - the public callback caps a request body, not the history table - and it can
                // carry anything the caller sends, including credentials. The run's trigger payload is
                // already persisted on the run and is where to read it from.
                PayloadJson = JsonSerializer.Serialize(
                    new
                    {
                        triggerEvent.InboundEventId,
                        CorrelationKey = correlationKey,
                        triggerEvent.ChannelKind,
                        MatchKeys = triggerEvent.MatchKeys,
                        triggerEvent.ReceivedAt
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Timestamp = nowUtc
            }
        ], ct);

            // Wrapped rather than calling the bus directly so Wbskt.Workflow carries no event-bus
            // dependency: the engine host supplies a publisher that forwards to MassTransit, and the
            // management host and every unit test get a no-op without needing a broker.
            await _runStartedPublisher.PublishAsync(createdRun, ct);

            return (createdRun.Id, createdBranch.Id);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start run for workflow definition {WorkflowDefinitionId} and trigger {TriggerNodeId}", workflowDefinitionId, triggerNodeId);
            throw;
        }
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
