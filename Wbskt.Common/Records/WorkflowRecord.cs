using Wbskt.Common.Enums;

namespace Wbskt.Common.Records;

public record WorkflowRecord
{
    public int Id { get; init; }
    public Guid RefId { get; init; }
    public int UserId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsEnabled { get; init; }
    public required TriggerType TriggerType { get; init; }
    public string? TriggerConfiguration { get; init; }
    public DateTime LastModified { get; init; }
}
