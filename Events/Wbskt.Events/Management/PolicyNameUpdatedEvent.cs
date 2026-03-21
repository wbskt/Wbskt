using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnPolicyNameUpdatedEvent")]
public sealed record PolicyNameUpdatedEvent(Guid PolicyRefId, int PolicyId, int WorkspaceId, string NewName, string OldName) : BaseEvent, IPolicyContext;
