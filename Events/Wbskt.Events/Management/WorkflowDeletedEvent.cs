using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowDeleted")]
public sealed record WorkflowDeletedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId) : BaseEvent, IWorkflowContext;
