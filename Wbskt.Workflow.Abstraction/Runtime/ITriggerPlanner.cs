using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Turns a version's trigger nodes into the registrations and schedules it has while it is live. Writes
/// nothing: enabling the version applies the plan in the same transaction as the state change.
/// </summary>
public interface ITriggerPlanner
{
    // workspaceRef scopes webhook trigger keys so an author-chosen path (e.g. "orders") is unique
    // per workspace and cannot collide with, or be triggered through, another workspace's webhook.
    TriggerPlan Plan(WorkflowDefinition definition, Guid workflowRefId, Guid workspaceRef);
}

public sealed record TriggerPlan(IReadOnlyCollection<PlannedTrigger> Registrations, IReadOnlyCollection<PlannedSchedule> Schedules)
{
    public static readonly TriggerPlan Empty = new([], []);
}

/// <param name="ConcurrencyPolicy">Stored form, from <c>TriggerRegistrationRow.FormatConcurrencyPolicy</c>.</param>
/// <param name="FilterExpression">Serialized <c>WorkflowExpression</c>, copied onto the row so the dispatcher need not load the definition.</param>
/// <param name="WebhookSecret">The plaintext, still written for one release so an engine rolled back to the previous image does not read a guarded webhook as open.</param>
/// <param name="WebhookSecretHash">What the dispatcher compares a caller's presented hash against.</param>
public sealed record PlannedTrigger(
    Guid TriggerNodeId,
    string TriggerKind,
    string TriggerKey,
    string? CorrelationExpression,
    string ConcurrencyPolicy,
    string? FilterExpression,
    string? WebhookSecret,
    string? WebhookSecretHash);

public sealed record PlannedSchedule(Guid TriggerNodeId, string CronOrInterval, DateTime NextFireAt);
