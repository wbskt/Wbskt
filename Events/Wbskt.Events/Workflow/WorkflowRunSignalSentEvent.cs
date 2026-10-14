using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

/// <summary>Someone sent a signal to a run through the API.</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowRunSignalSentEvent(Guid RunRefId, int WorkspaceId, string SignalName)
    : ActorEvent, IWorkspaceContext;
