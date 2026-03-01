namespace Webskt.Events.Client;

public record ClientPingEvent(Guid ClientRefId, int WorkspaceId, DateTime PingTime) : ClientPayloadEvent(ClientRefId, WorkspaceId, "ping", PingTime.ToLongTimeString());
