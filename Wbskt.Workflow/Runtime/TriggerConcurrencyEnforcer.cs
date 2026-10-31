using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
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
        // Parsed before anything else so a corrupt policy surfaces on every event, not only on the
        // ones that happen to find a run already active.
        WorkflowConcurrencyPolicy policy = registration.ParseConcurrencyPolicy();

        if (string.IsNullOrEmpty(evt.CorrelationKey))
        {
            return new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, TriggerConcurrencyDecision.NoRuns);
        }

        IReadOnlyCollection<Abstraction.Entities.RunRow> activeRuns = await _runProvider.GetActiveByCorrelationAsync(registration.WorkflowRefId, registration.TriggerNodeId, evt.CorrelationKey, ct);
        if (activeRuns.Count == 0)
        {
            return new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, TriggerConcurrencyDecision.NoRuns);
        }

        return policy switch
        {
            WorkflowConcurrencyPolicy.AllowParallel => new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, TriggerConcurrencyDecision.NoRuns),
            WorkflowConcurrencyPolicy.DropIfRunning => new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Dropped, TriggerConcurrencyDecision.NoRuns),
            WorkflowConcurrencyPolicy.Queue => await QueueAsync(registration, evt, ct),
            // Cancel every active run matching this correlation, not just the first - a stale
            // "cancel one, leave the rest running" bug when more than one run matched.
            WorkflowConcurrencyPolicy.CancelExisting => new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.ProceedAfterCancellingActive, activeRuns.Select(run => (long)run.Id).ToArray()),
            _ => throw new InvalidOperationException($"Concurrency policy {policy} has no enforcement.")
        };
    }

    private async Task<TriggerConcurrencyDecision> QueueAsync(Abstraction.Entities.TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
    {
        string inboundEventJson = JsonSerializer.Serialize(evt, EngineJson.Options);
        Abstraction.Entities.PendingTriggerEventRow? queued = await _pendingTriggerEventProvider.EnqueueBehindActiveRunAsync(registration.WorkflowRefId, registration.TriggerNodeId, evt.CorrelationKey ?? string.Empty, inboundEventJson, ct);

        // The run it would have waited for finished in the meantime, and nothing else holds the key.
        return queued is null
            ? new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, TriggerConcurrencyDecision.NoRuns)
            : new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Queued, TriggerConcurrencyDecision.NoRuns);
    }
}
