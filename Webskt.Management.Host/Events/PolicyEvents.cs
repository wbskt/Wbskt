using Webskt.Common.Abstraction.Events;

namespace Webskt.Management.Host.Events;

public record PolicyCreatedEvent(Guid PolicyRefId, string Name) : RegistrationPolicyEvent(PolicyRefId);
