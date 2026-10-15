using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Entities;

public record TriggerRegistrationRow
{
    public required int Id { get; init; }
    public required int WorkflowDefinitionId { get; init; }
    public required Guid WorkflowRefId { get; init; }
    public required int WorkflowVersion { get; init; }
    public required Guid TriggerNodeId { get; init; }
    public required string TriggerKind { get; init; }
    public required string TriggerKey { get; init; }
    public required string? CorrelationExpression { get; init; }

    /// <summary>
    /// The trigger's <see cref="WorkflowConcurrencyPolicy"/>, stored as the enum member's name. Written
    /// only through <see cref="FormatConcurrencyPolicy"/> and read only through
    /// <see cref="ParseConcurrencyPolicy"/>, so the two sides cannot drift apart.
    /// </summary>
    public required string ConcurrencyPolicy { get; init; }
    /// <summary>Serialized <c>WorkflowExpression</c>, evaluated against the inbound payload before a run starts.</summary>
    public required string? FilterExpression { get; init; }

    /// <summary>
    /// Shared secret a webhook caller must present. Not required (null) and meaningless for non-webhook
    /// kinds. Optional on the row so existing registrations keep working without one.
    /// </summary>
    public string? WebhookSecret { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>The stored form of a policy: the enum member's exact name.</summary>
    public static string FormatConcurrencyPolicy(WorkflowConcurrencyPolicy policy)
    {
        return policy.ToString();
    }

    /// <summary>
    /// The registration's policy. The engine writes this column from the enum and nothing else, so a
    /// value that is not an exact member name is corruption: it throws rather than falling back. An
    /// unreadable policy used to be treated as AllowParallel, which silently started parallel runs for a
    /// trigger whose author had asked for at most one.
    /// </summary>
    public WorkflowConcurrencyPolicy ParseConcurrencyPolicy()
    {
        // Matched by exact name rather than Enum.TryParse, which also accepts other casings and numbers.
        foreach (WorkflowConcurrencyPolicy policy in Enum.GetValues<WorkflowConcurrencyPolicy>())
        {
            if (string.Equals(ConcurrencyPolicy, FormatConcurrencyPolicy(policy), StringComparison.Ordinal))
            {
                return policy;
            }
        }

        throw new InvalidOperationException($"Trigger registration {Id} has unreadable concurrency policy '{ConcurrencyPolicy}'.");
    }
}
