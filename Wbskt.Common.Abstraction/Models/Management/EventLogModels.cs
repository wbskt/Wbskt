using Wbskt.EventBus.Abstractions;

namespace Wbskt.Common.Abstraction.Models.Management;

public record EventLogResponse(
    string EventName,
    string EventData,
    EventCriticality Criticality,
    Guid? PolicyRefId,
    Guid? ClientRefId,
    Guid? WorkflowRefId,
    DateTime CreatedAtUtc
);
