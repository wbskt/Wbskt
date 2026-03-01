using Webskt.EventBus.Abstractions;

namespace Webskt.Events.Abstractions;

public abstract record ClientLifecycleEvent(Guid ClientRefId, int WorkspaceId) : BaseEvent;