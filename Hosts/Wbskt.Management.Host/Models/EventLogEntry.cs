using Wbskt.Events.Abstractions;

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
    Guid? UserRefId = null,
    // The bus message it came from; the insert skips one already logged, so a redelivery is harmless.
    Guid? MessageId = null,
    EventSource? Source = null,
    // The caller's address and user agent, for an action taken through the API. Kept on the row, not
    // in EventData, so retention and privacy rules for them apply in one place.
    string? ClientAddress = null,
    string? UserAgent = null
);
