using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class TriggerDispatcher : ITriggerDispatcher
{
    private readonly ICorrelationKeyResolver _correlationKeyResolver;
    private readonly IBookmarkResumer _bookmarkResumer;
    private readonly ITriggerRegistrationProvider _triggerRegistrationProvider;

    public TriggerDispatcher(
        ICorrelationKeyResolver correlationKeyResolver,
        IBookmarkResumer bookmarkResumer,
        ITriggerRegistrationProvider triggerRegistrationProvider)
    {
        _correlationKeyResolver = correlationKeyResolver;
        _bookmarkResumer = bookmarkResumer;
        _triggerRegistrationProvider = triggerRegistrationProvider;
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

        return new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, null, null, correlationKey);
    }
}
