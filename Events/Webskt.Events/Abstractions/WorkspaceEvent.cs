using Webskt.EventBus.Abstractions;

namespace Webskt.Events.Abstractions;

public abstract record WorkspaceEvent(int WorkspaceId) : BaseEvent;
