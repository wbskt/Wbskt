using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>Someone asked for a run to be cancelled. Not the engine's internal cancellation message (<see cref="WorkflowRunCancellationRequestedEvent"/>).</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowRunCancelRequestedEvent(Guid RunRefId, int WorkspaceId, string Reason)
    : ActorEvent, IWorkspaceContext;
