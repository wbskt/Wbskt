namespace Wbskt.Management.Models.Workflow;

public record RunDetailDto(RunSummaryDto Summary, IReadOnlyList<BranchSummaryDto> Branches);