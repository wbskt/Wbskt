using Webskt.Common.Abstraction.Models.Management;

namespace Webskt.Common.Abstraction.Events.Shared;

public record ClientStatusChangedEvent(
    Guid ClientRefId, 
    ClientStatus OldStatus, 
    ClientStatus NewStatus
) : ClientLifecycleEvent(ClientRefId);
