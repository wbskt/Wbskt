using Wbskt.EventBus;

namespace Wbskt.Common.Events;

public class WorkflowStepsChangedEvent : IEvent
{
    public int WorkflowId { get; init; }
}
