namespace Wbskt.Common.Records;

public record WorkflowExecutionRecord
{
    public int Id { get; init; }
    public Guid WorkflowRefId { get; init; }
    public required string Status { get; init; }
    public DateTime TriggeredAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? InitialContext { get; init; }
    public string? ErrorLog { get; init; }
}
