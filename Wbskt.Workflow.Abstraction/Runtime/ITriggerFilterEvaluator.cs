using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Evaluates a trigger registration's filter against an inbound payload, before any run exists.
/// </summary>
public interface ITriggerFilterEvaluator
{
    /// <summary>
    /// True when the registration has no filter or its filter evaluates to <c>true</c>. Anything else,
    /// including a filter that cannot be evaluated or does not produce a boolean, is false.
    /// </summary>
    Task<bool> PassesAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct);
}
