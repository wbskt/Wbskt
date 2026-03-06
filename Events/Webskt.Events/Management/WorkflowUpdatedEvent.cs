using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowUpdatedEvent(Guid WorkflowRefId, int WorkspaceId) : WorkflowEvent(WorkflowRefId, WorkspaceId);