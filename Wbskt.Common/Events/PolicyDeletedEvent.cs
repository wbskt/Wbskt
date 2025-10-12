using Wbskt.EventBus;

namespace Wbskt.Common.Events;

public class PolicyDeletedEvent : IEvent
{
    public Guid PolicyRefId { get; init; }
    public int UserId { get; init; }
}
