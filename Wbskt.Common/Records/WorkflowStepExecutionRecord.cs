namespace Wbskt.Common.Records;

public record WorkflowStepExecutionRecord
{
    public int Id { get; init; }
    public int WorkflowExecutionId { get; init; }
    public int WorkflowStepId { get; init; }
    public required string Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? InputContext { get; init; }
    public string? OutputContext { get; init; }
    public string? ErrorLog { get; init; }
}
