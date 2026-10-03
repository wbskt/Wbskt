namespace Wbskt.Workflow.Engine.Host.Providers;

/// <summary>A hold whose time is up, leased for dispatch.</summary>
public sealed record ClientHoldStateRow
{
    public required int Id { get; init; }
    public required string TriggerKey { get; init; }
    public required DateTime SinceAt { get; init; }
    public required DateTime DueAt { get; init; }

    /// <summary>The latest matching message's trigger payload, serialized.</summary>
    public required string Payload { get; init; }
}
