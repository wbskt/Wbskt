using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowCreated")]
public sealed record WorkflowCreatedEvent(Guid WorkflowRefId, int WorkspaceId, string Name) : WorkflowEvent(WorkflowRefId, WorkspaceId);