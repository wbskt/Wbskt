using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

/// <summary>Someone was invited to the tenant, optionally with a role.</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record InvitationSentEvent(Guid InvitationRefId, string Email, Guid? RoleRefId, DateTime ExpiresAtUtc) : TenantActorEvent;

/// <summary>An outstanding invitation was withdrawn before anyone used it.</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record InvitationRevokedEvent(Guid InvitationRefId, string Email) : TenantActorEvent;

/// <summary>An invitation was used: <see cref="UserRefId"/> joined the tenant. The joiner is the actor.</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record InvitationAcceptedEvent(Guid InvitationRefId, string Email, int UserId, Guid UserRefId) : TenantActorEvent, IUserContext;

/// <summary>An invitation ran out unused. Logged at the moment it expired, by the auth host's sweep.</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record InvitationExpiredEvent(Guid InvitationRefId, string Email) : TenantActorEvent;
