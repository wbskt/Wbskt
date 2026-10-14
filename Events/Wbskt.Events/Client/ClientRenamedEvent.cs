using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientRenamedEvent")]
public sealed record ClientRenamedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string OldName, string NewName) : ActorEvent, IClientContext;
