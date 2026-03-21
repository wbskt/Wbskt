using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientConnectedEvent(Guid ClientRefId, int ClientId, int WorkspaceId) : BaseEvent, IClientContext;
