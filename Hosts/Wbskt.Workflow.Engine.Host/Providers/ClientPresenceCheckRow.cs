namespace Wbskt.Workflow.Engine.Host.Providers;

/// <summary>
/// A parked presence change, plus the client's presence as it stood when the check was leased.
/// The client columns are null when the client has since been deleted.
/// </summary>
public sealed record ClientPresenceCheckRow
{
    public required int Id { get; init; }
    public required string TriggerKey { get; init; }
    public required Guid ClientRefId { get; init; }
    public required string State { get; init; }
    public required DateTime ChangedAt { get; init; }
    public required DateTime DueAt { get; init; }
    public required int? ClientId { get; init; }
    public required int? ClientWorkspaceId { get; init; }
    public required bool? ClientIsConnected { get; init; }
    public required DateTime? ClientConnectedAt { get; init; }
    public required DateTime? ClientLastActivityAt { get; init; }
}
