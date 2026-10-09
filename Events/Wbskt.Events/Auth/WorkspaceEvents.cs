using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record WorkspaceCreatedEvent(int WorkspaceId, Guid WorkspaceRefId, string Name) : ActorEvent, IWorkspaceContext;

/// <summary>The workspace's name or description changed; <see cref="ActorEvent.Changes"/> says which.</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record WorkspaceUpdatedEvent(int WorkspaceId) : ActorEvent, IWorkspaceContext;

/// <summary>The workspace has a new owner. <see cref="ActorEvent.Changes"/> has the owners' user references.</summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record WorkspaceOwnershipTransferredEvent(int WorkspaceId, Guid NewOwnerRefId) : ActorEvent, IWorkspaceContext;

[EventCriticality(EventCriticality.Info)]
public sealed record WorkspaceMemberAddedEvent(int WorkspaceId, Guid MemberRefId) : ActorEvent, IWorkspaceContext;

[EventCriticality(EventCriticality.Info)]
public sealed record WorkspaceMemberRemovedEvent(int WorkspaceId, Guid MemberRefId) : ActorEvent, IWorkspaceContext;
