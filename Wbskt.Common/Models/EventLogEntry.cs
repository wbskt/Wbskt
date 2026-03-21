namespace Wbskt.Common.Models;

public sealed record EventLogEntry(
    int EventId, 
    string EventData, 
    DateTime CreatedAtUtc, 
    int? WorkspaceId,
    int? PolicyId = null,
    Guid? PolicyRefId = null,
    int? ClientId = null,
    Guid? ClientRefId = null,
    int? WorkflowId = null,
    Guid? WorkflowRefId = null
);
