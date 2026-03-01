using Webskt.Events.Abstractions;

namespace Webskt.Events.Management;

public record WorkflowDeletedEvent(Guid WorkflowRefId, int WorkspaceId) : WorkflowEvent(WorkflowRefId, WorkspaceId);