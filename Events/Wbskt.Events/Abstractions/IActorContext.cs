using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Abstractions;

/// <summary>
/// An event a person caused through the API (renamed a device, published a workflow…), carrying who.
/// The event log records the actor in its UserId/UserRefId columns. Both are null when the event was
/// raised without a signed-in user, such as by a workflow or a background job.
/// </summary>
public interface IActorContext : IEvent
{
    [SignalRPrivate] int? ActorUserId { get; }
    Guid? ActorUserRefId { get; }
}

/// <summary>
/// Base for <see cref="IActorContext"/> events. The actor is not passed by each publisher: the
/// management host's event bus stamps it from the signed-in user when the event is published (see
/// <c>ActorStampingEventBus</c>), so a new event of this kind is attributed without extra wiring.
/// </summary>
public abstract record ActorEvent : BaseEvent, IActorContext
{
    [SignalRPrivate] public int? ActorUserId { get; set; }
    public Guid? ActorUserRefId { get; set; }
}
