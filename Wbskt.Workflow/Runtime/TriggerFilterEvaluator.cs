using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class TriggerFilterEvaluator(IExpressionEvaluator expressionEvaluator, ILogger<TriggerFilterEvaluator> logger) : ITriggerFilterEvaluator
{
    public Task<bool> PassesAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct) =>
        PassesAsync(expressionEvaluator, logger, registration, evt, ct);

    /// <summary>
    /// Evaluates the registration's filter against the inbound payload, before any run exists. Returns
    /// true when there is no filter.
    /// </summary>
    internal static async Task<bool> PassesAsync(IExpressionEvaluator? expressionEvaluator, ILogger? logger, TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(registration.FilterExpression) || expressionEvaluator is null)
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

            JsonElement result = await expressionEvaluator.EvaluateAsync(filter, context, ct);
            if (result.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return result.ValueKind == JsonValueKind.True;
            }

            logger?.LogWarning("Filter on registration {RegistrationId} produced {Kind}, not a boolean; treating the event as non-matching.", registration.Id, result.ValueKind);
            return false;
        }
        catch (Exception ex)
        {
            // Fail closed. A filter is a gate, and a gate that cannot be evaluated has not been passed -
            // starting the run anyway would defeat the point of having one. Logged at warning because a
            // filter that never evaluates is a definition bug the author needs to see.
            logger?.LogWarning(ex, "Filter on registration {RegistrationId} could not be evaluated; treating the event as non-matching.", registration.Id);
            return false;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyState = new Dictionary<string, JsonElement>();
}
