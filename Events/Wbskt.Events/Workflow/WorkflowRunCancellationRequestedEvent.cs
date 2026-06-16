using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowRunCancellationRequestedEvent(long RunId, string Reason) : BaseEvent;
