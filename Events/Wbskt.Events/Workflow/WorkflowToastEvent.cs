using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>
/// A toast raised by an <c>action:toast</c> node. Unlike <c>action:clientMessage</c>, a toast has no
/// device target - it is addressed to the humans watching the workspace, so it fans out to the
/// workspace's SignalR group rather than to a client connection.
///
/// Carrying <see cref="IWorkflowContext"/> (not just the workspace) lets a dashboard link the toast
/// back to the run that raised it.
///
/// Note the known hub limitation recorded in Docs/API.Endpoints.md §2.8: the notification feed is
/// scoped per workspace, not per permission, so any workspace member with a live hub connection
/// receives this - do not put anything in Title/Message that a member should not see.
/// </summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowToastEvent")]
public sealed record WorkflowToastEvent(
    Guid WorkflowRefId,
    int WorkflowId,
    Guid RunRefId,
    int WorkspaceId,
    string Title,
    string Message)
    : BaseEvent, IWorkflowContext;
