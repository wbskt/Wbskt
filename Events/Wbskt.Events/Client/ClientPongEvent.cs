using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPongEvent(Guid ClientRefId, int ClientId, int WorkspaceId, DateTime OriginalPingTime) : BaseEvent, IClientContext;
