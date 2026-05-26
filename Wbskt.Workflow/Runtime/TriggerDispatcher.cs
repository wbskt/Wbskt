using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class TriggerDispatcher : ITriggerDispatcher
{
    private readonly ICorrelationKeyResolver _correlationKeyResolver;
    private readonly IBookmarkResumer _bookmarkResumer;
    private readonly ITriggerRegistrationProvider _triggerRegistrationProvider;
    private readonly ITriggerConcurrencyEnforcer _triggerConcurrencyEnforcer;
    private readonly IRunCancellationService _runCancellationService;

    public TriggerDispatcher(
        ICorrelationKeyResolver correlationKeyResolver,
        IBookmarkResumer bookmarkResumer,
        ITriggerRegistrationProvider triggerRegistrationProvider,
        ITriggerConcurrencyEnforcer triggerConcurrencyEnforcer,
        IRunCancellationService runCancellationService)
    {
        _correlationKeyResolver = correlationKeyResolver;
        _bookmarkResumer = bookmarkResumer;
        _triggerRegistrationProvider = triggerRegistrationProvider;
        _triggerConcurrencyEnforcer = triggerConcurrencyEnforcer;
        _runCancellationService = runCancellationService;
    }

    public async Task<TriggerDispatchResult> DispatchAsync(InboundEvent evt, CancellationToken ct)
    {
        string correlationKey = _correlationKeyResolver.Resolve(evt);
        InboundEvent normalizedEvent = evt with { CorrelationKey = correlationKey };

        BookmarkMatchResult bookmarkMatch = await _bookmarkResumer.MatchInboundAsync(normalizedEvent, ct);
        if (bookmarkMatch.Matched)
        {
            return new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, null, bookmarkMatch.BookmarkId, correlationKey);
        }

        if (bookmarkMatch.Idempotent)
        {
            return new TriggerDispatchResult(TriggerDispatchOutcome.Idempotent, null, null, correlationKey);
        }

        IReadOnlyCollection<TriggerRegistrationRow> registrations = await _triggerRegistrationProvider.GetActiveByChannelAsync(normalizedEvent.ChannelKind, correlationKey, ct);
        if (registrations.Count == 0)
        {
            return new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, correlationKey);
        }

        TriggerRegistrationRow registration = registrations.First();
        TriggerConcurrencyDecision concurrencyDecision = await _triggerConcurrencyEnforcer.EvaluateAsync(registration, normalizedEvent, ct);
        switch (concurrencyDecision.Outcome)
        {
            case TriggerConcurrencyOutcome.Dropped:
                return new TriggerDispatchResult(TriggerDispatchOutcome.Dropped, null, null, correlationKey);
            case TriggerConcurrencyOutcome.Queued:
                return new TriggerDispatchResult(TriggerDispatchOutcome.Queued, null, null, correlationKey);
            case TriggerConcurrencyOutcome.ProceedAfterCancellingActive when concurrencyDecision.RunIdToCancel.HasValue:
                await _runCancellationService.RequestCancellationAsync(concurrencyDecision.RunIdToCancel.Value, "Trigger-cancel-policy", ct);
                break;
        }

        return new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, null, null, correlationKey);
    }
}
