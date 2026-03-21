using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnNodeExecutionResumedEvent")]
public sealed record NodeExecutionResumedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, Guid NodeId) : BaseEvent, IWorkflowContext;
