namespace Wbskt.Management.Models.Workflow;

public record RunListResponse(IReadOnlyList<RunSummaryDto> Runs, long? NextCursor);