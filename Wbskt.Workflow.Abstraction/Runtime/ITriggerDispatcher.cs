namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ITriggerDispatcher
{
    Task<TriggerDispatchResult> DispatchAsync(InboundEvent evt, CancellationToken ct);
}

public sealed record TriggerDispatchResult(TriggerDispatchOutcome Outcome, long? RunId, long? BookmarkId, string Reason);

public enum TriggerDispatchOutcome
{
    ResumedBookmark,
    StartedRun,
    Queued,
    Dropped,
    Cancelled,
    NoRegistration,
    Idempotent
}
