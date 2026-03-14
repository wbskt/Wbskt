using Wbskt.Common.Abstraction;

namespace Wbskt.Events.Abstractions;

public abstract record UserEvent : AuthEvent
{
    protected UserEvent(int UserId)
    {
        this.UserId = UserId;
    }

    [SignalRPrivate]
    public int UserId { get; }
}