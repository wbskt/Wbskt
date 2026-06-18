namespace Wbskt.Management.Models.Workflow;

public record WorkflowSummaryDto(Guid RefId, int Version, string Status, string Name, string? Description, DateTime CreatedAt);
