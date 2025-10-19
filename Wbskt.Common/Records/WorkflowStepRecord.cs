using Wbskt.Common.Enums;

namespace Wbskt.Common.Records;

public record WorkflowStepRecord
{
    public int Id { get; init; }
    public int WorkflowId { get; init; }
    public int StepOrder { get; init; }
    public required string Name { get; init; }
    public required string StepType { get; init; }
    public required StepIdentifier StepIdentifier { get; init; }
    public string? StepConfiguration { get; init; }
    public int? OnSuccessStepId { get; init; }
    public int? OnFailureStepId { get; init; }
    public int? PositionX { get; init; }
    public int? PositionY { get; init; }
}
