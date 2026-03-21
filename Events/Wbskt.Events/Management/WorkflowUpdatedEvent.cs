using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowUpdated")]
public sealed record WorkflowUpdatedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId) : BaseEvent, IWorkflowContext;