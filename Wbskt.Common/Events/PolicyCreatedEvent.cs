using Wbskt.EventBus;

namespace Wbskt.Common.Events;

public class PolicyCreatedEvent : IEvent
{
    public Guid PolicyRefId { get; init; }
    public int UserId { get; init; }
}
