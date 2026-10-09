using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Abstractions;

/// <summary>
/// An event about a tenant's people or a person's own account, which belongs in the audit log of
/// several workspaces: the event log writes one row for each of <see cref="WorkspaceIds"/>. The
/// publisher lists them, since only the auth host knows which workspaces a tenant or a person has.
/// </summary>
public interface IWorkspacesContext : IEvent
{
    [SignalRPrivate] IReadOnlyList<int> WorkspaceIds { get; }
}

/// <summary>
/// Base for an action on a tenant's people (an invitation, a suspension, a role change), logged in
/// each of the tenant's workspaces and attributed like any <see cref="ActorEvent"/>.
/// </summary>
public abstract record TenantActorEvent : ActorEvent, IWorkspacesContext
{
    [SignalRPrivate] public IReadOnlyList<int> WorkspaceIds { get; init; } = [];
}
