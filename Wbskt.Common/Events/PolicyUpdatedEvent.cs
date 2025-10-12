using Wbskt.EventBus;

namespace Wbskt.Common.Events;

public class PolicyUpdatedEvent : IEvent
{
    public Guid PolicyRefId { get; init; }
    public int UserId { get; init; }
}
