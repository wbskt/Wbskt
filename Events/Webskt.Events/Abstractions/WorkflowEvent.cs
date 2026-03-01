using Webskt.EventBus.Abstractions;

namespace Webskt.Events.Abstractions;

public abstract record WorkflowEvent(Guid WorkflowRefId, int WorkspaceId) : BaseEvent;