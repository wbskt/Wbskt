using Wbskt.EventBus.Abstractions;

namespace Wbskt.Common.Abstraction.Models.Management;

public record EventLogResponse(
    string EventName,
    string EventData,
    EventCriticality Criticality,
    DateTime CreatedAtUtc
);
