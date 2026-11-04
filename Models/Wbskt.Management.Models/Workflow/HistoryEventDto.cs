namespace Wbskt.Management.Models.Workflow;

public record HistoryEventDto(long HistoryEventId, DateTimeOffset Timestamp, string EventKind, string Severity, Guid? BranchRefId, Guid? NodeId, string? PayloadJson);