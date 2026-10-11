using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Workflow.Engine.Host.Controllers;

internal static class InboundDispatchProjection
{
    /// <summary>
    /// The response every inbound endpoint answers with. Every per-registration outcome is kept: one
    /// inbound event can match several registrations, so a single run would name one arbitrary winner
    /// and hide the rest. Runs are named by the RefIds the dispatcher already carries, so nothing is
    /// read back.
    /// </summary>
    public static InboundResponse ToResponse(TriggerDispatchResult result) => new(
        ToOutcome(result.Outcome),
        result.RunRefId,
        result.Registrations
            .Select(r => new InboundRegistrationOutcome(r.WorkflowRefId, ToOutcome(r.Outcome), r.RunRefId, r.CorrelationKey))
            .ToArray());

    private static InboundOutcome ToOutcome(TriggerDispatchOutcome outcome) => outcome switch
    {
        TriggerDispatchOutcome.ResumedBookmark => InboundOutcome.ResumedBookmark,
        TriggerDispatchOutcome.StartedRun => InboundOutcome.StartedRun,
        TriggerDispatchOutcome.Queued => InboundOutcome.Queued,
        TriggerDispatchOutcome.Dropped => InboundOutcome.Dropped,
        TriggerDispatchOutcome.Cancelled => InboundOutcome.Cancelled,
        TriggerDispatchOutcome.NoRegistration => InboundOutcome.NoRegistration,
        TriggerDispatchOutcome.Idempotent => InboundOutcome.Idempotent,
        TriggerDispatchOutcome.Filtered => InboundOutcome.Filtered,
        TriggerDispatchOutcome.SecretMismatch => InboundOutcome.SecretMismatch,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "No inbound outcome for this dispatch outcome.")
    };
}
