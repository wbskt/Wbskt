using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

/// <summary>
/// A version of a workflow was made live: <c>WorkflowId</c> is that version's definition. <c>PreviousVersion</c>
/// is the version it replaced, or null when the workflow was disabled.
/// </summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowEnabledEvent")]
public sealed record WorkflowEnabledEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, int Version, int? PreviousVersion)
    : ActorEvent, IWorkflowContext;
