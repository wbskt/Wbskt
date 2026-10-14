using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowRunStartedEvent")]
public sealed record WorkflowRunStartedEvent(Guid WorkflowRefId, int WorkflowId, Guid RunRefId, int WorkspaceId)
    : BaseEvent, IWorkflowContext;
