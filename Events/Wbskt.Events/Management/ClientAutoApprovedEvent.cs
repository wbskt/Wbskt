using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientAutoApprovedEvent")]
public sealed record ClientAutoApprovedEvent(Guid ClientRefId, int ClientId, Guid PolicyRefId, int PolicyId, int WorkspaceId) : BaseEvent, IClientContext, IPolicyContext;