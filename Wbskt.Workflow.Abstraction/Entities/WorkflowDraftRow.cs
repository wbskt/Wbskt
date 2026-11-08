namespace Wbskt.Workflow.Abstraction.Entities;

public record WorkflowDraftRow
{
    public required Guid RefId { get; init; }
    public required int WorkspaceId { get; init; }

    /// <summary>The version the draft was started from; null for a workflow never published.</summary>
    public required int? BaseVersion { get; init; }

    public required int Revision { get; init; }
    public required string Name { get; init; }
    public required string? Description { get; init; }

    /// <summary><c>{}</c> in a draft list, which carries no definition.</summary>
    public required string DefinitionJson { get; init; }

    public required int CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required int UpdatedBy { get; init; }
    public required Guid? UpdatedByRefId { get; init; }
    public required DateTime UpdatedAt { get; init; }

    /// <summary>The workflow's newest version now; null while it has never been published.</summary>
    public required int? CurrentVersion { get; init; }
}
