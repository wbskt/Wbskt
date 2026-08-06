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
    public required string ConcurrencyPolicy { get; init; }
    /// <summary>Serialized <c>WorkflowExpression</c>, evaluated against the inbound payload before a run starts.</summary>
    public required string? FilterExpression { get; init; }

    /// <summary>
    /// Shared secret a webhook caller must present. Not required (null) and meaningless for non-webhook
    /// kinds. Optional on the row so existing registrations keep working without one.
    /// </summary>
    public string? WebhookSecret { get; init; }

    public required DateTime CreatedAt { get; init; }
}
