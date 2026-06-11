using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

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

    public TriggerDispatcher(
        ICorrelationKeyResolver correlationKeyResolver,
        IBookmarkResumer bookmarkResumer,
        ITriggerRegistrationProvider triggerRegistrationProvider,
        ITriggerConcurrencyEnforcer triggerConcurrencyEnforcer,
        IRunCancellationService runCancellationService,
        IRunStarter runStarter,
        IRunDispatcher runDispatcher)
    {
        _correlationKeyResolver = correlationKeyResolver;
        _bookmarkResumer = bookmarkResumer;
        _triggerRegistrationProvider = triggerRegistrationProvider;
        _triggerConcurrencyEnforcer = triggerConcurrencyEnforcer;
        _runCancellationService = runCancellationService;
        _runStarter = runStarter;
        _runDispatcher = runDispatcher;
    }

    public async Task<TriggerDispatchResult> DispatchAsync(InboundEvent evt, CancellationToken ct)
    {
        string defaultCorrelation = _correlationKeyResolver.Resolve(evt);
        InboundEvent resolvedEvent = evt with { CorrelationKey = defaultCorrelation };

        BookmarkMatchResult bookmarkMatch = await _bookmarkResumer.MatchInboundAsync(resolvedEvent, ct);
        if (bookmarkMatch.Matched)
        {
            return new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, null, bookmarkMatch.BookmarkId, defaultCorrelation);
        }

        if (bookmarkMatch.Idempotent)
        {
            return new TriggerDispatchResult(TriggerDispatchOutcome.Idempotent, null, null, defaultCorrelation);
        }

        IReadOnlyCollection<TriggerRegistrationRow> registrations = await _triggerRegistrationProvider.GetActiveByChannelKeysAsync(resolvedEvent.ChannelKind, resolvedEvent.MatchKeys, ct);
        if (registrations.Count == 0)
        {
            return new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, defaultCorrelation);
        }

        TriggerDispatchOutcome aggregateOutcome = TriggerDispatchOutcome.NoRegistration;
        long? firstStartedRunId = null;
        string finalReason = defaultCorrelation;

        foreach (TriggerRegistrationRow registration in registrations)
        {
            string? correlationValue = EvaluateCorrelationExpression(registration.CorrelationExpression, resolvedEvent)
                ?? defaultCorrelation;
            InboundEvent normalizedEvent = resolvedEvent with { CorrelationKey = correlationValue };

            TriggerConcurrencyDecision concurrencyDecision = await _triggerConcurrencyEnforcer.EvaluateAsync(registration, normalizedEvent, ct);
            switch (concurrencyDecision.Outcome)
            {
                case TriggerConcurrencyOutcome.Dropped:
                    if (aggregateOutcome == TriggerDispatchOutcome.NoRegistration)
                    {
                        aggregateOutcome = TriggerDispatchOutcome.Dropped;
                        finalReason = correlationValue;
                    }
                    continue;

                case TriggerConcurrencyOutcome.Queued:
                    if (aggregateOutcome == TriggerDispatchOutcome.NoRegistration || aggregateOutcome == TriggerDispatchOutcome.Dropped)
                    {
                        aggregateOutcome = TriggerDispatchOutcome.Queued;
                        finalReason = correlationValue;
                    }
                    continue;

                case TriggerConcurrencyOutcome.ProceedAfterCancellingActive when concurrencyDecision.RunIdToCancel.HasValue:
                    await _runCancellationService.RequestCancellationAsync(concurrencyDecision.RunIdToCancel.Value, "Trigger-cancel-policy", ct);
                    break;
            }

            (long runId, long branchId) = await _runStarter.StartAsync(registration.WorkflowDefinitionId, registration.TriggerNodeId.ToString(), normalizedEvent, ct);
            await _runDispatcher.DispatchAsync(new BranchExecutionRequest(runId, branchId, BranchExecutionReason.TriggerStarted), ct);
            
            aggregateOutcome = TriggerDispatchOutcome.StartedRun;
            if (firstStartedRunId == null)
            {
                firstStartedRunId = runId;
                finalReason = correlationValue;
            }
        }

        return new TriggerDispatchResult(aggregateOutcome, firstStartedRunId, null, finalReason);
    }

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
