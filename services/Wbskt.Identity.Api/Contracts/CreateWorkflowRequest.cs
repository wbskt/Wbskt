using Wbskt.Common.Records;

namespace Wbskt.Identity.Api.Contracts;

public record CreateWorkflowRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsEnabled { get; init; }
    public required string TriggerType { get; init; }
    public string? TriggerConfiguration { get; init; }
    public required List<WorkflowStepRecord> Steps { get; init; }
}
