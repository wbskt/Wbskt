using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

/// <summary>A policy's registration PIN was replaced. Never carries the PIN.</summary>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnPolicyPinRotatedEvent")]
public sealed record PolicyPinRotatedEvent(Guid PolicyRefId, int PolicyId, int WorkspaceId) : BaseEvent, IPolicyContext;
