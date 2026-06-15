using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Telemetry;

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
    private readonly WorkflowMetrics? _workflowMetrics;

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
        WorkflowMetrics? workflowMetrics = null)
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
        _workflowMetrics = workflowMetrics;
    }

    public async Task<(long RunId, long BranchId)> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, CancellationToken ct)
    {
        WorkflowDefinitionRow definition = await _workflowDefinitionProvider.GetByIdAsync(workflowDefinitionId, ct);
        _workflowMetrics?.RecordRunStarted(definition.RefId.ToString(), triggerEvent.ChannelKind);
        DateTime nowUtc = _clock.UtcNow;
        string correlationKey = triggerEvent.CorrelationKey ?? _correlationKeyResolver.Resolve(triggerEvent);

        RunRow createdRun = await _runProvider.CreateAsync(new RunRow
        {
            Id = 0,
            RefId = _idGenerator.NewId(),
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
            CreditBudget = 100m,
            CreatedAt = nowUtc
        }, ct);

        // Account for the initial branch in the run's active-branch counter so the
        // run can finalize when this branch completes (mirrors the Fork path, which
        // counts the child branches it creates). Run_Create seeds the counter at 0.
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
            RowVersion = Array.Empty<byte>()
        }, ct);

        await _historyEventProvider.InsertBatchAsync([
            new HistoryEventRow
            {
                HistoryEventId = 0,
                RunId = createdRun.Id,
                BranchRefId = createdBranch.RefId,
                NodeId = Guid.Parse(triggerNodeId),
                EventKind = "RunStarted",
                Severity = "Info",
                PayloadJson = JsonSerializer.Serialize(new { triggerEvent.InboundEventId, CorrelationKey = correlationKey }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Timestamp = nowUtc
            }
        ], ct);

        await _runStartedPublisher.PublishAsync(createdRun, ct);

        return (createdRun.Id, createdBranch.Id);
    }
}
