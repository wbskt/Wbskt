using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

/// <summary>A member was removed from the tenant; workspaces they owned passed to whoever removed them.</summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record MemberRemovedEvent(Guid MemberRefId) : TenantActorEvent;

/// <summary>A member was suspended: they keep their account but lose access to this tenant.</summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record MemberSuspendedEvent(Guid MemberRefId) : TenantActorEvent;

/// <summary>A suspended member's access to the tenant was restored.</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record MemberUnsuspendedEvent(Guid MemberRefId) : TenantActorEvent;

/// <summary>
/// A member was given a role, tenant-wide or in one workspace (<see cref="WorkspaceRefId"/>).
/// </summary>
[EventCriticality(EventCriticality.Info)]
public sealed record MemberRoleAssignedEvent(Guid MemberRefId, Guid RoleRefId, Guid? WorkspaceRefId) : TenantActorEvent;

/// <summary>A member lost a role, tenant-wide or in one workspace (<see cref="WorkspaceRefId"/>).</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record MemberRoleRemovedEvent(Guid MemberRefId, Guid RoleRefId, Guid? WorkspaceRefId) : TenantActorEvent;

/// <summary>
/// A role's permissions were changed. <see cref="ActorEvent.Changes"/> has one "permissions" entry with
/// the role's permissions before and after, comma separated, a denied one written as "!slug".
/// </summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record RolePermissionsUpdatedEvent(Guid RoleRefId) : TenantActorEvent;
