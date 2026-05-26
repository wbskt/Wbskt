namespace Wbskt.Management.Models.Workflow;

public record RunSummaryDto(Guid RefId, Guid WorkflowDefinitionRefId, int Version, string Status, string? CorrelationKey, DateTime StartedAt, DateTime? CompletedAt);