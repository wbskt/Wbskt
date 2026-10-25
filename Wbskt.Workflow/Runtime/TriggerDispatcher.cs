using System.Security.Cryptography;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;
using Microsoft.Extensions.Logging;

namespace Wbskt.Workflow.Runtime;

internal sealed class TriggerDispatcher : ITriggerDispatcher
{
    private readonly ICorrelationKeyResolver _correlationKeyResolver;
    private readonly IBookmarkResumer _bookmarkResumer;
    private readonly ITriggerRegistrationProvider _triggerRegistrationProvider;
    private readonly ITriggerRegistrationCache _triggerRegistrationCache;
    private readonly ITriggerConcurrencyEnforcer _triggerConcurrencyEnforcer;
    private readonly IRunCancellationService _runCancellationService;
    private readonly IRunStarter _runStarter;
    private readonly IRunDispatcher _runDispatcher;
    private readonly IIdempotencyKeyProvider _idempotencyKeyProvider;
    private readonly IExpressionEvaluator _expressionEvaluator;
    private readonly ILogger<TriggerDispatcher>? _logger;

    public TriggerDispatcher(
        ICorrelationKeyResolver correlationKeyResolver,
        IBookmarkResumer bookmarkResumer,
        ITriggerRegistrationProvider triggerRegistrationProvider,
        ITriggerRegistrationCache triggerRegistrationCache,
        ITriggerConcurrencyEnforcer triggerConcurrencyEnforcer,
        IRunCancellationService runCancellationService,
        IRunStarter runStarter,
        IRunDispatcher runDispatcher,
        IIdempotencyKeyProvider idempotencyKeyProvider,
        IExpressionEvaluator expressionEvaluator,
        ILogger<TriggerDispatcher>? logger = null)
    {
        _correlationKeyResolver = correlationKeyResolver;
        _bookmarkResumer = bookmarkResumer;
        _triggerRegistrationProvider = triggerRegistrationProvider;
        _triggerRegistrationCache = triggerRegistrationCache;
        _triggerConcurrencyEnforcer = triggerConcurrencyEnforcer;
        _runCancellationService = runCancellationService;
        _runStarter = runStarter;
        _runDispatcher = runDispatcher;
        _idempotencyKeyProvider = idempotencyKeyProvider;
        _expressionEvaluator = expressionEvaluator;
        _logger = logger;
    }

    public async Task<TriggerDispatchResult> DispatchAsync(InboundEvent evt, CancellationToken ct)
    {
        string defaultCorrelation = _correlationKeyResolver.Resolve(evt);
        // At this point this is effectively the trigger key - no registration has been resolved yet, so
        // there is no per-trigger correlation expression to apply. It serves three purposes: the
        // registration lookup below, the idempotency claim, and bookmark matching. (The claim/bookmark
        // interaction looks alarming but is correct; BookmarkResumer explains why.)
        InboundEvent resolvedEvent = evt with { CorrelationKey = defaultCorrelation };

        // Registrations are read before the claim so an event that nothing listens for writes nothing
        // (see IBookmarkResumer.MatchInboundAsync); the same list is used below.
        IReadOnlyCollection<TriggerRegistrationRow> registrations = await GetRegistrationsAsync(resolvedEvent, ct);
        if (evt.ReplayFor is { } replayFor)
        {
            // A replayed queued event is for the trigger that queued it and no other (InboundEvent.ReplayFor).
            registrations = registrations
                .Where(r => r.WorkflowRefId == replayFor.WorkflowRefId && r.TriggerNodeId == replayFor.TriggerNodeId)
                .ToArray();
        }

        BookmarkMatchResult bookmarkMatch = await _bookmarkResumer.MatchInboundAsync(resolvedEvent, registrations.Count > 0, ct);
        if (bookmarkMatch.Matched)
        {
            _logger?.LogDebug("Event {EventId} resumed bookmark {BookmarkId}", evt.InboundEventId, bookmarkMatch.BookmarkId);
            return new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, null, bookmarkMatch.BookmarkId, defaultCorrelation);
        }

        if (bookmarkMatch.Idempotent)
        {
            _logger?.LogInformation("Event {EventId} was dropped due to idempotency duplicate", evt.InboundEventId);
            return new TriggerDispatchResult(TriggerDispatchOutcome.Idempotent, null, null, defaultCorrelation);
        }

        _logger?.LogInformation("Found {Count} active trigger registrations for channel {ChannelKind} and keys {MatchKeys}", registrations.Count, resolvedEvent.ChannelKind, resolvedEvent.MatchKeys);
        if (registrations.Count == 0)
        {
            _logger?.LogWarning("No active trigger registrations found for channel {ChannelKind} and keys {MatchKeys}", resolvedEvent.ChannelKind, resolvedEvent.MatchKeys);
            if (bookmarkMatch.ClaimKey != null)
            {
                // Otherwise this claim stays 'Pending' forever - a later redelivery of the same
                // event would then wait on a claim nobody will ever complete.
                _logger?.LogDebug("Marking idempotency claim key {ClaimKey} as succeeded", bookmarkMatch.ClaimKey);
                await _idempotencyKeyProvider.MarkSucceededAsync(bookmarkMatch.ClaimKey, "{}", ct);
            }
            return new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, defaultCorrelation);
        }

        List<TriggerRegistrationDispatch> perRegistration = new(registrations.Count);

        // A throw part-way through leaves the claim Pending, and a Pending claim drops every later
        // delivery with the same event id as a duplicate. With a caller-chosen id (a webhook's
        // Idempotency-Key) that would turn the sender's retry of a failed delivery into a silent
        // no-op, so the claim is released as Failed, which BookmarkResumer reclaims on the retry.
        // Each registration then takes a claim of its own (ClaimRegistrationAsync), so the retry
        // skips the registrations that already went through and only redoes the one that failed.
        try
        {
            foreach (TriggerRegistrationRow registration in registrations)
            {
                // The per-registration correlation key, which is what the concurrency policy correlates
                // *existing runs* against. Bookmarks played their part above and are done with: the bookmark
                // match runs first and returns early, so reaching this line means the event resumed nothing
                // and is being considered for starting something new.
                string? correlationValue = EvaluateCorrelationExpression(registration.CorrelationExpression, resolvedEvent)
                    ?? defaultCorrelation;
                _logger?.LogDebug("Evaluated correlation expression for registration {RegistrationId} to {CorrelationValue}", registration.Id, correlationValue);
                InboundEvent normalizedEvent = resolvedEvent with { CorrelationKey = correlationValue };

                // Secret and filter are both checked before the concurrency enforcer: neither should be able
                // to queue, drop or cancel anything. An event that fails either was never for this trigger.
                if (!SecretMatches(registration, resolvedEvent))
                {
                    // Recorded per-registration but never surfaced to the caller: the public callback answers
                    // 202 either way, so a wrong secret stays indistinguishable from a right one.
                    _logger?.LogWarning("Registration {RegistrationId} rejected event {EventId}: webhook secret mismatch.", registration.Id, evt.InboundEventId);
                    perRegistration.Add(new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.SecretMismatch, null, correlationValue));
                    continue;
                }

                if (!await FilterPassesAsync(registration, normalizedEvent, ct))
                {
                    // Reported rather than silently swallowed - "I fired the webhook and nothing happened" is
                    // otherwise unanswerable, and a filter is the most likely reason.
                    _logger?.LogDebug("Registration {RegistrationId} filtered out event {EventId}.", registration.Id, evt.InboundEventId);
                    perRegistration.Add(new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.Filtered, null, correlationValue));
                    continue;
                }

                string startKey = TriggerStartKeys.For(evt.InboundEventId, registration.Id);
                RegistrationClaim registrationClaim = await ClaimRegistrationAsync(startKey, ct);
                if (registrationClaim.Settled is not null)
                {
                    // An earlier attempt at this event already went through for this registration: report
                    // what it did rather than doing it again.
                    _logger?.LogInformation("Event {EventId} already dispatched to registration {RegistrationId}; skipping it", evt.InboundEventId, registration.Id);
                    perRegistration.Add(registrationClaim.Settled with { RegistrationId = registration.Id, WorkflowRefId = registration.WorkflowRefId });
                    continue;
                }

                if (!registrationClaim.Owned)
                {
                    _logger?.LogInformation("Event {EventId} is already being dispatched to registration {RegistrationId}; skipping it", evt.InboundEventId, registration.Id);
                    perRegistration.Add(new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.Idempotent, null, correlationValue));
                    continue;
                }

                TriggerRegistrationDispatch dispatch;
                long? branchToDispatch;
                try
                {
                    (dispatch, branchToDispatch) = await StartForRegistrationAsync(registration, normalizedEvent, correlationValue, startKey, registrationClaim.Reclaimed, ct);
                    await _idempotencyKeyProvider.MarkSucceededAsync(startKey, JsonSerializer.Serialize(dispatch, EngineJson.Options), ct);
                }
                catch
                {
                    await _idempotencyKeyProvider.MarkFailedAsync(startKey, "{\"error\":\"dispatch failed\"}", CancellationToken.None);
                    throw;
                }

                // Only after the claim is settled: a retry that reclaims this registration's claim knows
                // its run was never handed to the pump, so it can hand it over without running it twice.
                if (branchToDispatch is long branchId)
                {
                    await _runDispatcher.DispatchAsync(new BranchExecutionRequest(dispatch.RunId!.Value, branchId, BranchExecutionReason.TriggerStarted), ct);
                }

                perRegistration.Add(dispatch);
            }
        }
        catch when (bookmarkMatch.ClaimKey != null)
        {
            await _idempotencyKeyProvider.MarkFailedAsync(bookmarkMatch.ClaimKey, "{\"error\":\"dispatch failed\"}", CancellationToken.None);
            throw;
        }

        if (bookmarkMatch.ClaimKey != null)
        {
            _logger?.LogDebug("Marking idempotency claim key {ClaimKey} as succeeded", bookmarkMatch.ClaimKey);
            await _idempotencyKeyProvider.MarkSucceededAsync(bookmarkMatch.ClaimKey, "{}", ct);
        }

        // The summary is deliberately lossy - one event fanning out to several registrations can
        // legitimately start one run and drop another. Callers that care read Registrations; the
        // summary exists so the common single-registration case stays a single value.
        TriggerRegistrationDispatch? summary =
            perRegistration.FirstOrDefault(r => r.Outcome == TriggerDispatchOutcome.StartedRun)
            ?? perRegistration.FirstOrDefault(r => r.Outcome == TriggerDispatchOutcome.Queued)
            ?? perRegistration.FirstOrDefault(r => r.Outcome == TriggerDispatchOutcome.Dropped)
            ?? perRegistration.FirstOrDefault(r => r.Outcome == TriggerDispatchOutcome.Refused)
            ?? perRegistration.FirstOrDefault(r => r.Outcome == TriggerDispatchOutcome.Filtered)
            ?? perRegistration.FirstOrDefault(r => r.Outcome == TriggerDispatchOutcome.SecretMismatch);

        return new TriggerDispatchResult(
            summary?.Outcome ?? TriggerDispatchOutcome.NoRegistration,
            summary?.RunId,
            null,
            summary?.CorrelationKey ?? defaultCorrelation,
            perRegistration,
            summary?.RunRefId);
    }

    /// <summary>
    /// The registrations for the event's keys. For a device message, "nothing listens" may come from the
    /// cache, which is the answer most device messages get; registrations, which claim the event and start
    /// runs below, are always read from the database. A stale cache can therefore cost a read, or (until
    /// the change is announced) miss a trigger published a moment ago, but never act on a removed one.
    /// </summary>
    /// <remarks>
    /// Only device messages, the channel that arrives at the rate devices talk. Every other channel is
    /// read each time, so a webhook fired or a run started by hand right after a publish never waits on
    /// the announcement that clears the cache.
    /// </remarks>
    private Task<IReadOnlyCollection<TriggerRegistrationRow>> GetRegistrationsAsync(InboundEvent evt, CancellationToken ct)
    {
        if (!string.Equals(evt.ChannelKind, ClientChannel.Name, StringComparison.Ordinal) || evt.MatchKeys.Count == 0)
        {
            return _triggerRegistrationProvider.GetActiveByChannelKeysAsync(evt.ChannelKind, evt.MatchKeys, ct);
        }

        return _triggerRegistrationCache.LoadUnlessKnownEmptyAsync(
            evt.ChannelKind,
            string.Join('\n', evt.MatchKeys),
            token => _triggerRegistrationProvider.GetActiveByChannelKeysAsync(evt.ChannelKind, evt.MatchKeys, token),
            ct);
    }

    /// <summary>
    /// Applies the registration's concurrency policy and starts its run. Returns the branch to hand to
    /// the pump, if there is one; the caller dispatches it once this registration's claim is settled.
    /// </summary>
    private async Task<(TriggerRegistrationDispatch Dispatch, long? BranchToDispatch)> StartForRegistrationAsync(
        TriggerRegistrationRow registration,
        InboundEvent normalizedEvent,
        string correlationValue,
        string startKey,
        bool reclaimed,
        CancellationToken ct)
    {
        if (reclaimed)
        {
            // An earlier attempt failed for this registration. If it got as far as creating the run, that
            // run is this event's: the concurrency policy already let it through, and evaluating the policy
            // again would count it as the active run to drop, queue behind or cancel.
            RunStartResult? existing = await _runStarter.FindStartedAsync(startKey, ct);
            if (existing is not null)
            {
                _logger?.LogInformation("Event {EventId} recovered run {RunId} for registration {RegistrationId}", normalizedEvent.InboundEventId, existing.RunId, registration.Id);
                return (new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.StartedRun, existing.RunId, correlationValue, existing.RunRefId), existing.BranchId);
            }
        }

        TriggerConcurrencyDecision concurrencyDecision = await _triggerConcurrencyEnforcer.EvaluateAsync(registration, normalizedEvent, ct);
        _logger?.LogInformation("Concurrency decision for registration {RegistrationId} is {Outcome} (RunIdsToCancel: {RunIdsToCancel})", registration.Id, concurrencyDecision.Outcome, concurrencyDecision.RunIdsToCancel);
        switch (concurrencyDecision.Outcome)
        {
            case TriggerConcurrencyOutcome.Dropped:
                return (new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.Dropped, null, correlationValue), null);

            case TriggerConcurrencyOutcome.Queued:
                return (new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.Queued, null, correlationValue), null);

            case TriggerConcurrencyOutcome.ProceedAfterCancellingActive:
                foreach (long runIdToCancel in concurrencyDecision.RunIdsToCancel)
                {
                    await _runCancellationService.RequestCancellationAsync(runIdToCancel, "Trigger-cancel-policy", ct);
                }
                break;
        }

        // Some starts take a place under the workspace's active-run ceiling that is already held, so
        // refusing them would lose work rather than shed it: a sub-workflow child (its parent is running),
        // a queued event's replay (the run that finished made the room, and the event has left the queue),
        // and a start replacing the runs it has just cancelled (refusing it would leave nothing running).
        bool exemptFromCeiling = normalizedEvent.StartsChildRun
            || normalizedEvent.ReplayFor is not null
            || (concurrencyDecision.Outcome == TriggerConcurrencyOutcome.ProceedAfterCancellingActive && concurrencyDecision.RunIdsToCancel.Count > 0);

        RunStartResult started;
        try
        {
            started = await _runStarter.StartAsync(registration.WorkflowDefinitionId, registration.TriggerNodeId.ToString(), normalizedEvent, startKey, ct, exemptFromCeiling);
        }
        catch (WorkspaceRunCeilingException)
        {
            // Settled like a drop: the claim records it, so a redelivered event is not refused twice
            // or, worse, started late once the workspace has room.
            return (new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.Refused, null, correlationValue), null);
        }

        _logger?.LogInformation("Event {EventId} started run {RunId} on branch {BranchId} for registration {RegistrationId}", normalizedEvent.InboundEventId, started.RunId, started.BranchId, registration.Id);

        // A run that already existed under this key without this registration's claim saying so outlived
        // its claim (the claims are swept after IdempotencyRetentionWindow, the runs are not), so it has
        // long since been handed to the pump.
        return (new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.StartedRun, started.RunId, correlationValue, started.RunRefId),
            started.Created ? started.BranchId : null);
    }

    /// <summary>
    /// Claims starting this event for one registration. <see cref="RegistrationClaim.Settled"/> carries
    /// what an earlier attempt did when it went through; a claim another attempt holds is not owned.
    /// </summary>
    private async Task<RegistrationClaim> ClaimRegistrationAsync(string startKey, CancellationToken ct)
    {
        // Same token-as-claim scheme as the event's own claim; BookmarkResumer explains it.
        Guid claimToken = Guid.NewGuid();
        IdempotencyKeyRow claim = await _idempotencyKeyProvider.UpsertPendingAsync(startKey, 0, claimToken, Guid.Empty, 0, ct);
        if (claim.BranchRefId == claimToken)
        {
            return new RegistrationClaim(true, false, null);
        }

        if (string.Equals(claim.Status, IdempotencyKeyStatus.Succeeded, StringComparison.Ordinal))
        {
            TriggerRegistrationDispatch? settled = claim.ResultJson is null
                ? null
                : JsonSerializer.Deserialize<TriggerRegistrationDispatch>(claim.ResultJson, EngineJson.Options);
            return new RegistrationClaim(false, false, settled);
        }

        if (string.Equals(claim.Status, IdempotencyKeyStatus.Failed, StringComparison.Ordinal))
        {
            claim = await _idempotencyKeyProvider.ReclaimFailedAsync(startKey, claimToken, ct);
            return new RegistrationClaim(claim.BranchRefId == claimToken, true, null);
        }

        // Pending under another token: an attempt that is still going, or one that crashed before it
        // could settle. Either way this attempt must not start the registration a second time.
        return new RegistrationClaim(false, false, null);
    }

    private sealed record RegistrationClaim(bool Owned, bool Reclaimed, TriggerRegistrationDispatch? Settled);

    /// <summary>
    /// Constant-time comparison against the registration's secret. A registration without one is open,
    /// which is what every webhook published before secrets existed reads as.
    /// </summary>
    private static bool SecretMatches(TriggerRegistrationRow registration, InboundEvent evt)
    {
        if (string.IsNullOrEmpty(registration.WebhookSecret))
        {
            return true;
        }

        // The caller's secret arrives hashed, so the configured one is hashed the same way. Fixed-time so
        // the comparison cannot be used to recover the hash a character at a time.
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(WebhookSecretHash.Of(registration.WebhookSecret)!),
            System.Text.Encoding.UTF8.GetBytes(evt.SecretHash ?? string.Empty));
    }

    private Task<bool> FilterPassesAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct) =>
        TriggerFilterEvaluator.PassesAsync(_expressionEvaluator, _logger, registration, evt, ct);

    /// <summary>
    /// Resolves a registration's correlation expression against the arriving payload.
    /// </summary>
    /// <remarks>
    /// Two forms, and only two: a dotted path into the payload (<c>$trigger.payload.room</c> or
    /// <c>{{trigger.payload.room}}</c>, see <see cref="CorrelationKeyExpression"/>), or any other string
    /// taken as a constant. A path that walks off the end of the payload returns null, and the caller
    /// falls back to the trigger key. The validator rejects a template that is not a payload path
    /// (`INVALID_CORRELATION_KEY`) and warns on other '$' strings (`SUSPICIOUS_CORRELATION_KEY`), since
    /// either would otherwise be one constant shared by every run.
    /// </remarks>
    private static string? EvaluateCorrelationExpression(string? expression, InboundEvent evt)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        if (CorrelationKeyExpression.TryGetTriggerPath(expression, out string path))
        {
            string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            if (!evt.Payload.TryGetValue(segments[0], out JsonElement current))
            {
                return null;
            }

            for (int i = 1; i < segments.Length; i++)
            {
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segments[i], out JsonElement next))
                {
                    current = next;
                }
                else
                {
                    return null;
                }
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
        }

        return expression; // Constant
    }
}
