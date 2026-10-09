using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

/// <summary>A disabled registration policy was turned back on, so devices can join through it again.</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record PolicyEnabledEvent(Guid PolicyRefId, int PolicyId, int WorkspaceId) : ActorEvent, IPolicyContext;
