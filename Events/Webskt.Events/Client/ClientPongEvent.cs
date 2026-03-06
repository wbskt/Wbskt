using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPongEvent(Guid ClientRefId, int WorkspaceId, DateTime OriginalPingTime) : ClientEvent(ClientRefId, WorkspaceId);
