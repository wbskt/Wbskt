using System.Security.Cryptography;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Expressions;
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

        foreach (TriggerRegistrationRow registration in registrations)
        {
            // [RJ]: this is where the co-relation key for co-relating existing runs are evaluated. I still wonder how bookmarks above plays a role.
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

    /// <summary>
    /// Evaluates the registration's filter against the inbound payload, before any run exists. Returns
    /// true when there is no filter.
    /// </summary>
    private async Task<bool> FilterPassesAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(registration.FilterExpression) || _expressionEvaluator is null)
        {
            return true;
        }

        try
        {
            WorkflowExpression? filter = JsonSerializer.Deserialize<WorkflowExpression>(registration.FilterExpression, SerializerOptions);
            if (filter is null)
            {
                return true;
            }

            // There is no branch yet, so the context is a shim over the payload: $trigger resolves,
            // branch-local state is empty, and the ids that only mean something inside a run are zero.
            var context = new BranchContext(
                RunId: 0,
                BranchId: 0,
                WorkflowDefinitionId: registration.WorkflowDefinitionId,
                WorkflowDefinitionRefId: registration.WorkflowRefId,
                Version: registration.WorkflowVersion,
                CurrentNodeId: registration.TriggerNodeId.ToString(),
                Attempt: 0,
                LocalState: EmptyState,
                TriggerPayload: evt.Payload,
                CorrelationKey: evt.CorrelationKey ?? string.Empty,
                StartedAt: evt.ReceivedAt,
                WorkspaceId: 0);

            JsonElement result = await _expressionEvaluator.EvaluateAsync(filter, context, ct);
            if (result.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return result.ValueKind == JsonValueKind.True;
            }

            _logger?.LogWarning("Filter on registration {RegistrationId} produced {Kind}, not a boolean; treating the event as non-matching.", registration.Id, result.ValueKind);
            return false;
        }
        catch (Exception ex)
        {
            // Fail closed. A filter is a gate, and a gate that cannot be evaluated has not been passed -
            // starting the run anyway would defeat the point of having one. Logged at warning because a
            // filter that never evaluates is a definition bug the author needs to see.
            _logger?.LogWarning(ex, "Filter on registration {RegistrationId} could not be evaluated; treating the event as non-matching.", registration.Id);
            return false;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyState = new Dictionary<string, JsonElement>();

    // [RJ]: I'm gonna trust this for now. will review later.
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
