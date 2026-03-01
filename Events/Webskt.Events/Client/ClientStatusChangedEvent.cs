using Webskt.Common.Abstraction.Models.Management;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientStatusChangedEvent(Guid ClientRefId, int WorkspaceId, ClientStatus Status) : ClientLifecycleEvent(ClientRefId, WorkspaceId);
