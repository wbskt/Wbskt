namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>Where a client trigger with a hold time stands (<c>dbo.ClientHoldStates</c>).</summary>
public record TriggerHoldStateRow
{
    /// <summary>'holding', 'fired' or 'clear'.</summary>
    public required string State { get; init; }

    public DateTime? SinceAt { get; init; }
    public DateTime? DueAt { get; init; }
    public required DateTime LastEventAt { get; init; }
}
