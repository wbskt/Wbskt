using System.Text.Json;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class TriggerConcurrencyEnforcer : ITriggerConcurrencyEnforcer
{
    private readonly IRunProvider _runProvider;
    private readonly IPendingTriggerEventProvider _pendingTriggerEventProvider;

    public TriggerConcurrencyEnforcer(IRunProvider runProvider, IPendingTriggerEventProvider pendingTriggerEventProvider)
    {
        _runProvider = runProvider;
        _pendingTriggerEventProvider = pendingTriggerEventProvider;
    }

    public async Task<TriggerConcurrencyDecision> EvaluateAsync(Abstraction.Entities.TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
    {
        IReadOnlyCollection<Abstraction.Entities.RunRow> activeRuns = await _runProvider.GetActiveByCorrelationAsync(registration.WorkflowRefId, registration.TriggerNodeId, evt.CorrelationKey ?? string.Empty, ct);
        if (activeRuns.Count == 0)
        {
            return new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, null);
        }

        return registration.ConcurrencyPolicy switch
        {
            "DropIfRunning" => new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Dropped, null),
            "Queue" => await QueueAsync(registration, evt, ct),
            "CancelExisting" => new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.ProceedAfterCancellingActive, activeRuns.First().Id),
            _ => new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, null)
        };
    }

    private async Task<TriggerConcurrencyDecision> QueueAsync(Abstraction.Entities.TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
    {
        string inboundEventJson = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await _pendingTriggerEventProvider.EnqueueAsync(registration.WorkflowRefId, registration.TriggerNodeId, evt.CorrelationKey ?? string.Empty, inboundEventJson, ct);
        return new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Queued, null);
    }
}
