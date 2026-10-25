using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Management;

/// <summary>A disabled registration policy was turned back on, so devices can join through it again.</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnPolicyEnabledEvent")]
public sealed record PolicyEnabledEvent(Guid PolicyRefId, int PolicyId, int WorkspaceId) : ActorEvent, IPolicyContext;
