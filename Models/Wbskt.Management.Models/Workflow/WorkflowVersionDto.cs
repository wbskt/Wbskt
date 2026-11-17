namespace Wbskt.Management.Models.Workflow;

/// <summary>One version in a workflow's history. <c>IsLive</c> marks the one that starts runs, if any.</summary>
public record WorkflowVersionDto(int Version, bool IsLive, string Name, string? Description, long RunCount, DateTimeOffset CreatedAt);
