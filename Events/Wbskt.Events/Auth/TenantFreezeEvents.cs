using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Auth;

/// <summary>
/// Wbskt support froze the tenant: its members can still sign in and read, but no workspace in it takes
/// a change, its running workflow runs are cancelled and its clients are revoked. Always from
/// <see cref="EventSource.Support"/>.
/// </summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record TenantFrozenEvent : TenantActorEvent
{
    public TenantFrozenEvent(Guid tenantRefId)
    {
        TenantRefId = tenantRefId;
        ActorSource = EventSource.Support;
    }

    public Guid TenantRefId { get; init; }
}

/// <summary>
/// Wbskt support lifted the tenant's freeze. Clients revoked by the freeze stay revoked until approved
/// again. Always from <see cref="EventSource.Support"/>.
/// </summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record TenantUnfrozenEvent : TenantActorEvent
{
    public TenantUnfrozenEvent(Guid tenantRefId)
    {
        TenantRefId = tenantRefId;
        ActorSource = EventSource.Support;
    }

    public Guid TenantRefId { get; init; }
}
