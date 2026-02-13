using Webskt.Events.Abstractions;

namespace Webskt.Events.Shared;

public record DeviceCommandDeliveredEvent(Guid ClientRefId, string Action) : DeviceControlEvent(ClientRefId);

public record DeviceCommandFailedEvent(Guid ClientRefId, string Action, string Reason) : DeviceControlEvent(ClientRefId);
