namespace Webskt.Common.Abstraction.Events;

public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}
