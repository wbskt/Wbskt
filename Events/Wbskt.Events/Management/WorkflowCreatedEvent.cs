using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowCreated")]
public sealed record WorkflowCreatedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, string Name) : BaseEvent, IWorkflowContext;