namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ITriggerDispatcher
{
    Task<TriggerDispatchResult> DispatchAsync(InboundEvent evt, CancellationToken ct);
}

/// <summary>
/// The result of dispatching one inbound event.
/// </summary>
/// <param name="Outcome">
/// A summary of <paramref name="Registrations"/>, by precedence: <c>StartedRun</c> if any registration
/// started a run, else <c>Queued</c>, else <c>Dropped</c>, else <c>NoRegistration</c>. A summary of a
/// fan-out is necessarily lossy - read <paramref name="Registrations"/> for what each one actually did.
/// </param>
/// <param name="RunId">
/// The first run started, kept for callers that expect a single run. When several registrations matched,
/// use <see cref="TriggerDispatchResult.StartedRunIds"/> instead - this names only one of them.
/// </param>
/// <param name="Registrations">
/// One entry per matched trigger registration, in the order they were processed. Empty for the
/// bookmark-resume, idempotent-duplicate and no-registration paths, which never reach registration
/// processing.
/// </param>
public sealed record TriggerDispatchResult(
    TriggerDispatchOutcome Outcome,
    long? RunId,
    long? BookmarkId,
    string Reason,
    IReadOnlyList<TriggerRegistrationDispatch>? Registrations = null)
{
    public IReadOnlyList<TriggerRegistrationDispatch> Registrations { get; init; } = Registrations ?? [];

    /// <summary>Every run this event started, in registration order.</summary>
    public IReadOnlyList<long> StartedRunIds =>
        Registrations.Where(r => r.RunId.HasValue).Select(r => r.RunId!.Value).ToArray();
}

/// <summary>What one trigger registration did with the event.</summary>
public sealed record TriggerRegistrationDispatch(
    long RegistrationId,
    Guid WorkflowRefId,
    TriggerDispatchOutcome Outcome,
    long? RunId,
    string CorrelationKey);

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
