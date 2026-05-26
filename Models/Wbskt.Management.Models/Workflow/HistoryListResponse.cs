namespace Wbskt.Management.Models.Workflow;

public record HistoryListResponse(IReadOnlyList<HistoryEventDto> Events, long? NextCursor);