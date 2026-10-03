using System.Security.Cryptography;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Microsoft.Extensions.Logging;

namespace Wbskt.Workflow.Runtime;

internal sealed class TriggerDispatcher : ITriggerDispatcher
{
    private readonly ICorrelationKeyResolver _correlationKeyResolver;
    private readonly IBookmarkResumer _bookmarkResumer;
    private readonly ITriggerRegistrationProvider _triggerRegistrationProvider;
    private readonly ITriggerConcurrencyEnforcer _triggerConcurrencyEnforcer;
    private readonly IRunCancellationService _runCancellationService;
    private readonly IRunStarter _runStarter;
    private readonly IRunDispatcher _runDispatcher;
    private readonly IIdempotencyKeyProvider _idempotencyKeyProvider;
    private readonly IExpressionEvaluator? _expressionEvaluator;
    private readonly ILogger<TriggerDispatcher>? _logger;

    public TriggerDispatcher(
        ICorrelationKeyResolver correlationKeyResolver,
        IBookmarkResumer bookmarkResumer,
        ITriggerRegistrationProvider triggerRegistrationProvider,
        ITriggerConcurrencyEnforcer triggerConcurrencyEnforcer,
        IRunCancellationService runCancellationService,
        IRunStarter runStarter,
        IRunDispatcher runDispatcher,
        IIdempotencyKeyProvider idempotencyKeyProvider,
        IExpressionEvaluator? expressionEvaluator = null,
        ILogger<TriggerDispatcher>? logger = null)
    {
        _correlationKeyResolver = correlationKeyResolver;
        _bookmarkResumer = bookmarkResumer;
        _triggerRegistrationProvider = triggerRegistrationProvider;
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

        BookmarkMatchResult bookmarkMatch = await _bookmarkResumer.MatchInboundAsync(resolvedEvent, ct);
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

        IReadOnlyCollection<TriggerRegistrationRow> registrations = await _triggerRegistrationProvider.GetActiveByChannelKeysAsync(resolvedEvent.ChannelKind, resolvedEvent.MatchKeys, ct);
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
        // no-op, so the claim is released as Failed, which BookmarkResumer reclaims on the retry. The
        // cost is that a registration which did start a run before the throw starts another on retry.
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

                TriggerConcurrencyDecision concurrencyDecision = await _triggerConcurrencyEnforcer.EvaluateAsync(registration, normalizedEvent, ct);
                _logger?.LogInformation("Concurrency decision for registration {RegistrationId} is {Outcome} (RunIdsToCancel: {RunIdsToCancel})", registration.Id, concurrencyDecision.Outcome, concurrencyDecision.RunIdsToCancel);
                switch (concurrencyDecision.Outcome)
                {
                    case TriggerConcurrencyOutcome.Dropped:
                        perRegistration.Add(new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.Dropped, null, correlationValue));
                        continue;

                    case TriggerConcurrencyOutcome.Queued:
                        perRegistration.Add(new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.Queued, null, correlationValue));
                        continue;

                    case TriggerConcurrencyOutcome.ProceedAfterCancellingActive:
                        foreach (long runIdToCancel in concurrencyDecision.RunIdsToCancel)
                        {
                            await _runCancellationService.RequestCancellationAsync(runIdToCancel, "Trigger-cancel-policy", ct);
                        }
                        break;
                }

                (long runId, long branchId) = await _runStarter.StartAsync(registration.WorkflowDefinitionId, registration.TriggerNodeId.ToString(), normalizedEvent, ct);
                _logger?.LogInformation("Event {EventId} started run {RunId} on branch {BranchId} for registration {RegistrationId}", normalizedEvent.InboundEventId, runId, branchId, registration.Id);
                await _runDispatcher.DispatchAsync(new BranchExecutionRequest(runId, branchId, BranchExecutionReason.TriggerStarted), ct);

                perRegistration.Add(new TriggerRegistrationDispatch(registration.Id, registration.WorkflowRefId, TriggerDispatchOutcome.StartedRun, runId, correlationValue));
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
            ?? perRegistration.FirstOrDefault(r => r.Outcome == TriggerDispatchOutcome.Filtered)
            ?? perRegistration.FirstOrDefault(r => r.Outcome == TriggerDispatchOutcome.SecretMismatch);

        return new TriggerDispatchResult(
            summary?.Outcome ?? TriggerDispatchOutcome.NoRegistration,
            summary?.RunId,
            null,
            summary?.CorrelationKey ?? defaultCorrelation,
            perRegistration);
    }

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

        // Fixed-time so the comparison cannot be used to recover the secret a character at a time.
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(registration.WebhookSecret),
            System.Text.Encoding.UTF8.GetBytes(evt.Secret ?? string.Empty));
    }

    private Task<bool> FilterPassesAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct) =>
        TriggerFilterEvaluator.PassesAsync(_expressionEvaluator, _logger, registration, evt, ct);

    /// <summary>
    /// Resolves a registration's correlation expression against the arriving payload.
    /// </summary>
    /// <remarks>
    /// Two forms, and only two: a <c>$trigger.</c>-prefixed dotted path into the payload, or any other
    /// string taken as a constant. Anything that walks off the end of the payload returns null, and the
    /// caller falls back to the trigger key - so a mistyped path silently correlates everything together
    /// rather than nothing. That is why the validator warns (`SUSPICIOUS_CORRELATION_KEY`) on a
    /// correlation key that is neither <c>$trigger.</c>-prefixed nor an obvious constant.
    /// </remarks>
    private static string? EvaluateCorrelationExpression(string? expression, InboundEvent evt)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        if (expression.StartsWith("$trigger.", StringComparison.OrdinalIgnoreCase))
        {
            string path = expression.Substring("$trigger.".Length);
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
