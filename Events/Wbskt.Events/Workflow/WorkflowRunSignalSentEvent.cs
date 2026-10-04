using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>Someone sent a signal to a run through the API.</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowRunSignalSentEvent(Guid RunRefId, int WorkspaceId, string SignalName)
    : ActorEvent, IWorkspaceContext;
