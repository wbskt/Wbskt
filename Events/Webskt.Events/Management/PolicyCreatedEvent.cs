using Webskt.Events.Abstractions;

namespace Webskt.Events.Management;

public record PolicyCreatedEvent(Guid PolicyRefId, int WorkspaceId, string Name) : RegistrationPolicyEvent(PolicyRefId, WorkspaceId);
