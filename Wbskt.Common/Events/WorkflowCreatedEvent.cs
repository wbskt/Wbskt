using Wbskt.EventBus;

namespace Wbskt.Common.Events;

public class WorkflowCreatedEvent : IEvent
{
    public Guid WorkflowRefId { get; init; }
    public int UserId { get; init; }
}
