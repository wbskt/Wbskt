using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Infrastructure.Events;

/// <summary>
/// Records who caused an <see cref="ActorEvent"/>: on publish, an event with no actor yet gets the
/// signed-in user of the current request. It must sit in front of the queue, while the request's
/// identity is still in scope; by the time a queued event reaches the broker it is gone. An event
/// published with no signed-in user (a consumer, a background job) passes through unchanged.
/// </summary>
public sealed class ActorStampingEventBus(IEventBus inner, IIdentityService identityService) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        if (@event is ActorEvent { ActorUserId: null } actorEvent && identityService.TryGetUserIdentity(out var identity))
        {
            actorEvent.ActorUserId = identity.UserId;
            actorEvent.ActorUserRefId = identity.UserRefId;
        }

        return inner.PublishAsync(@event, ct);
    }
}
