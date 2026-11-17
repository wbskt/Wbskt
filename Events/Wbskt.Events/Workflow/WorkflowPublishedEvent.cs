using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

/// <summary>
/// A version of a workflow was published. <c>Enabled</c> says whether it was asked to be made live with it;
/// that is recorded on its own as a <see cref="WorkflowEnabledEvent"/>.
/// </summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowPublishedEvent")]
public sealed record WorkflowPublishedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, int Version, string Name, bool Enabled)
    : ActorEvent, IWorkflowContext;
