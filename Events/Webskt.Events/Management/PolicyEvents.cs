using Webskt.Events.Abstractions;

namespace Webskt.Events.Management;

public record PolicyCreatedEvent(Guid PolicyRefId, string Name) : RegistrationPolicyEvent(PolicyRefId);
