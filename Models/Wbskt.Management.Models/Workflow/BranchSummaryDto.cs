namespace Wbskt.Management.Models.Workflow;

public record BranchSummaryDto(Guid RefId, string Status, Guid NodeId, DateTime CreatedAt, DateTime UpdatedAt);