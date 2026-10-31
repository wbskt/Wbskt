using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Management;

/// <summary>
/// A registration policy with no clients left was deleted. Its PIN registers nothing from now on,
/// templates pinned to it were unpinned, and the event log keeps its history under
/// <see cref="PolicyRefId"/>.
/// </summary>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnPolicyDeletedEvent")]
public sealed record PolicyDeletedEvent(Guid PolicyRefId, int PolicyId, int WorkspaceId, string Name) : ActorEvent, IPolicyContext;
