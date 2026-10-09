using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Infrastructure.Events;

/// <summary>
/// Records who caused an <see cref="ActorEvent"/>, and from where: on publish, an event with no actor
/// yet gets the signed-in user of the current request, the request's source (console or API), and
/// its address and user agent. It must sit in front of the queue, while the request is still in
/// scope; by the time a queued event reaches the broker it is gone. An event published with no
/// signed-in user (a consumer, a background job) passes through unchanged, as does one whose
/// publisher already named its actor (a workflow run).
/// </summary>
public sealed class ActorStampingEventBus(IEventBus inner, IIdentityService identityService, IRequestOriginAccessor? requestOrigin = null) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        if (@event is ActorEvent { ActorUserId: null, ActorSource: null } actorEvent && identityService.TryGetUserIdentity(out var identity))
        {
            actorEvent.ActorUserId = identity.UserId;
            actorEvent.ActorUserRefId = identity.UserRefId;

            if (requestOrigin?.Current is { } origin)
            {
                actorEvent.ActorSource = origin.Source;
                actorEvent.ClientAddress = origin.ClientAddress;
                actorEvent.UserAgent = origin.UserAgent;
            }
        }

        return inner.PublishAsync(@event, ct);
    }
}
