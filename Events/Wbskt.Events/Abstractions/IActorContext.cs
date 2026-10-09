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

    /// <summary>Where the action came from. Stamped with the actor; set by the publisher for a workflow run.</summary>
    public EventSource? ActorSource { get; set; }

    /// <summary>The workflow whose run took the action, when <see cref="ActorSource"/> is a workflow.</summary>
    public Guid? ActorWorkflowRefId { get; set; }

    /// <summary>The run that took the action, when <see cref="ActorSource"/> is a workflow.</summary>
    public Guid? ActorRunRefId { get; set; }

    /// <summary>
    /// The caller's address, for an action taken through the API. The event log moves it to its own
    /// column and out of the stored event, so retention and privacy rules apply to it in one place;
    /// it is never pushed to browsers.
    /// </summary>
    [SignalRPrivate] public string? ClientAddress { get; set; }

    /// <summary>The caller's user agent, kept and withheld exactly like <see cref="ClientAddress"/>.</summary>
    [SignalRPrivate] public string? UserAgent { get; set; }

    /// <summary>What a change event changed, field by field; null for an event that is not a change.</summary>
    public IReadOnlyList<FieldChange>? Changes { get; init; }
}
