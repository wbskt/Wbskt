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
        if (string.IsNullOrEmpty(evt.CorrelationKey))
        {
            return new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, TriggerConcurrencyDecision.NoRuns);
        }

        IReadOnlyCollection<Abstraction.Entities.RunRow> activeRuns = await _runProvider.GetActiveByCorrelationAsync(registration.WorkflowRefId, registration.TriggerNodeId, evt.CorrelationKey, ct);
        if (activeRuns.Count == 0)
        {
            return new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, TriggerConcurrencyDecision.NoRuns);
        }

        return registration.ConcurrencyPolicy switch
        {
            "DropIfRunning" => new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Dropped, TriggerConcurrencyDecision.NoRuns),
            "Queue" => await QueueAsync(registration, evt, ct),
            // Cancel every active run matching this correlation, not just the first - a stale
            // "cancel one, leave the rest running" bug when more than one run matched.
            "CancelExisting" => new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.ProceedAfterCancellingActive, activeRuns.Select(run => (long)run.Id).ToArray()),
            _ => new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, TriggerConcurrencyDecision.NoRuns)
        };
    }

    private async Task<TriggerConcurrencyDecision> QueueAsync(Abstraction.Entities.TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
    {
        string inboundEventJson = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await _pendingTriggerEventProvider.EnqueueAsync(registration.WorkflowRefId, registration.TriggerNodeId, evt.CorrelationKey ?? string.Empty, inboundEventJson, ct);
        return new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Queued, TriggerConcurrencyDecision.NoRuns);
    }
}
