namespace Wbskt.Management.Host.Models;

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
    Guid? WorkflowRefId = null,
    int? UserId = null,
    Guid? UserRefId = null
);
