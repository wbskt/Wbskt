namespace Webskt.Common.Abstraction.Events.Shared;

public record DeviceCommandDeliveredEvent(Guid ClientRefId, string Action) : DeviceControlEvent(ClientRefId);

public record DeviceCommandFailedEvent(Guid ClientRefId, string Action, string Reason) : DeviceControlEvent(ClientRefId);
