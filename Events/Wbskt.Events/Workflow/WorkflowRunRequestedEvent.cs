using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>
/// Someone started a run by hand. <c>Outcome</c> is the engine's answer (Started, Queued, Duplicate);
/// the run itself is recorded by <see cref="WorkflowRunStartedEvent"/> once it begins.
/// </summary>
[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowRunRequestedEvent(Guid WorkflowRefId, int WorkspaceId, Guid? RunRefId, string Outcome)
    : ActorEvent, IWorkspaceContext;
