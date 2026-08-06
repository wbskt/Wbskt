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
            _workflowMetrics?.RecordRunStarted(definition.RefId.ToString(), triggerEvent.ChannelKind);
            decimal creditBudget = ResolveCreditBudget(definition.DefinitionJson);
            DateTime nowUtc = _clock.UtcNow;
        string correlationKey = triggerEvent.CorrelationKey ?? _correlationKeyResolver.Resolve(triggerEvent); // [RJ]: we actually will always have c-key in the trigger event at this point. don't need to use resolver.

        RunRow createdRun = await _runProvider.CreateAsync(new RunRow
        {
            Id = 0,
            RefId = _idGenerator.NewId(), // [RJ]: using external for easier testing
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
        
        // [RJ]: TODO: is this working? i dont think so. since its matching by id. at this time there are no entries in the table with this id.
        // [RJ]: EDIT: dbo.Run_Create in CreateAsync ensures the dbo.RunCounters has entry. so we good.
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
            RowVersion = Array.Empty<byte>() // [RJ]: TODO: bytes? also not used anywhere.
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
                // [RJ]: TODO: dont we need the actual payload here?
                PayloadJson = JsonSerializer.Serialize(new { triggerEvent.InboundEventId, CorrelationKey = correlationKey }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Timestamp = nowUtc
            }
        ], ct);

            // [RJ]: TODO: just fire the freaking event through the e-bus here. why wrap it in another service?
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
