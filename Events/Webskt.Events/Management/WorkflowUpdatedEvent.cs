using Webskt.Events.Abstractions;

namespace Webskt.Events.Management;

public record WorkflowUpdatedEvent(Guid WorkflowRefId, int WorkspaceId) : WorkflowEvent(WorkflowRefId, WorkspaceId);