using Webskt.Events.Abstractions;

namespace Webskt.Events.Management;

public record WorkflowCreatedEvent(Guid WorkflowRefId, int WorkspaceId) : WorkflowEvent(WorkflowRefId, WorkspaceId);

public record WorkflowUpdatedEvent(Guid WorkflowRefId, int WorkspaceId) : WorkflowEvent(WorkflowRefId, WorkspaceId);

public record WorkflowDeletedEvent(Guid WorkflowRefId, int WorkspaceId) : WorkflowEvent(WorkflowRefId, WorkspaceId);
