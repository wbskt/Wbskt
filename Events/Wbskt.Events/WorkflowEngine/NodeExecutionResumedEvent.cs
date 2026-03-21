using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Info)]
public sealed record NodeExecutionResumedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, Guid NodeId) : BaseEvent, IWorkflowContext;
