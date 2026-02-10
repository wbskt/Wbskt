namespace Webskt.Common.Abstraction.Events;

public interface IEvent
{
    DateTime CreatedAtUtc { get; }
}

public abstract record BaseEvent : IEvent
{
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
