using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientPongEvent(Guid ClientRefId, int WorkspaceId, DateTime OriginalPingTime) : ClientEvent(ClientRefId, WorkspaceId);
