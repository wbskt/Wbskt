namespace Wbskt.Workflow.Abstraction.Entities;

public record RunCountersRow
{
    public required int RunId { get; init; }
    public required int ActiveBranchCount { get; init; }
    public required decimal CreditsConsumed { get; init; }
    public required DateTime UpdatedAt { get; init; }
}
