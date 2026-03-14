using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowUpdated")]
public sealed record WorkflowUpdatedEvent(Guid WorkflowRefId, int WorkspaceId) : WorkflowEvent(WorkflowRefId, WorkspaceId);