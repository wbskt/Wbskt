using Wbskt.EventBus.Abstractions;

namespace Wbskt.Management.Host.Models;

public record EventLogResponse(
    string EventName,
    string EventData,
    EventCriticality Criticality,
    Guid? PolicyRefId,
    Guid? ClientRefId,
    Guid? WorkflowRefId,
    DateTime CreatedAtUtc
);
