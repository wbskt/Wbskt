using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientAutoApprovedEvent")]
public sealed record ClientAutoApprovedEvent(Guid ClientRefId, int WorkspaceId) : ClientEvent(ClientRefId, WorkspaceId);
