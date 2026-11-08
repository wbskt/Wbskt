using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowRunStartedEvent")]
/// <param name="RunNumber">The run's number within its workflow (#1, #2, ...).</param>
public sealed record WorkflowRunStartedEvent(Guid WorkflowRefId, int WorkflowId, Guid RunRefId, int WorkspaceId, int RunNumber = 0)
    : BaseEvent, IWorkflowContext;
