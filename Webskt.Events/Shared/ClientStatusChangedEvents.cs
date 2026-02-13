using Webskt.Common.Abstraction.Models.Management;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Shared;

public record ClientStatusChangedEvent(
    Guid ClientRefId, 
    ClientStatus OldStatus, 
    ClientStatus NewStatus
) : ClientLifecycleEvent(ClientRefId);
