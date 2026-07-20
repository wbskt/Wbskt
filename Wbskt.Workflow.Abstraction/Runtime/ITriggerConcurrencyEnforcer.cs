using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ITriggerConcurrencyEnforcer
{
    Task<TriggerConcurrencyDecision> EvaluateAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct);
}

public sealed record TriggerConcurrencyDecision(TriggerConcurrencyOutcome Outcome, IReadOnlyCollection<long> RunIdsToCancel)
{
    public static readonly IReadOnlyCollection<long> NoRuns = Array.Empty<long>();
}

public enum TriggerConcurrencyOutcome
{
    Proceed,
    Queued,
    Dropped,
    ProceedAfterCancellingActive
}
