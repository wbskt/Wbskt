namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// One version in a workflow's history. Status is <c>Published</c> or <c>Deprecated</c> for the
/// newest version and <c>Superseded</c> for the rest.
/// </summary>
public record WorkflowVersionDto(int Version, string Status, string Name, string? Description, long RunCount, DateTime CreatedAt);
