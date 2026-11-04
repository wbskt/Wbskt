namespace Wbskt.Management.Models.Workflow;

public record BranchSummaryDto(Guid RefId, string Status, Guid NodeId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? LocalJson = null);