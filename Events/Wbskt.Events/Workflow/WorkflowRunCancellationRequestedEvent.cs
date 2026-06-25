using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Workflow;

[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowRunCancellationRequestedEvent(long RunId, string Reason) : BaseEvent;
