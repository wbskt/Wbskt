namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>One published version of a workflow, without its definition JSON.</summary>
public sealed class WorkflowVersionRow
{
    public required int Version { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required bool IsEnabled { get; init; }
    public required int PublishedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required long RunCount { get; init; }
}

/// <summary>What deleting a workflow left for the caller to do outside the database.</summary>
public sealed record WorkflowDeletion(IReadOnlyList<long> ActiveRunIds, IReadOnlyList<int> DefinitionIds);
