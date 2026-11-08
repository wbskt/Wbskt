namespace Wbskt.Workflow.Abstraction.Entities;

public record WorkflowDefinitionRow
{
    public required int Id { get; init; }
    public required Guid RefId { get; init; }
    public required int Version { get; init; }
    public required int WorkspaceId { get; init; }
    public required string Name { get; init; }
    public required string? Description { get; init; }
    public required bool IsEnabled { get; init; }
    public required string DefinitionJson { get; init; }
    public required int PublishedBy { get; init; }
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// The definition's trigger node kinds (<c>trigger:client</c>, ...), distinct and in node order. Only the
    /// summary list reads it, since it carries no <see cref="DefinitionJson"/>; null elsewhere.
    /// </summary>
    public IReadOnlyList<string>? TriggerNodeKinds { get; init; }

    /// <summary>The workflow's draft, when it has one. Only the summary list reads it; null elsewhere.</summary>
    public WorkflowDraftSummary? Draft { get; init; }
}
