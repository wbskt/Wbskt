namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>What one workflow trigger did with one message (<c>dbo.TriggerDispatchOutcomes</c>).</summary>
public record TriggerDispatchOutcomeRow
{
    /// <summary>
    /// The outcome a client trigger with a hold time records when its filter matched: the run starts
    /// once it has kept matching for the whole hold time. The dispatcher never sees such a message.
    /// </summary>
    public const string HeldOutcome = "Held";

    public required Guid WorkflowRefId { get; init; }
    public required Guid TriggerNodeId { get; init; }
    public required DateTime OccurredAt { get; init; }
    public required string ChannelKind { get; init; }
    public string? MessageType { get; init; }

    /// <summary>A <see cref="Wbskt.Workflow.Abstraction.Runtime.TriggerDispatchOutcome"/> name, or <see cref="HeldOutcome"/>.</summary>
    public required string Outcome { get; init; }

    public string? CorrelationKey { get; init; }
    public Guid? RunRefId { get; init; }
}
