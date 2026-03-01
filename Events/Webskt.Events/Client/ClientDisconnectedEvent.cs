using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientDisconnectedEvent(Guid ClientRefId, int WorkspaceId, string Reason) : ClientEvent(ClientRefId, WorkspaceId);
