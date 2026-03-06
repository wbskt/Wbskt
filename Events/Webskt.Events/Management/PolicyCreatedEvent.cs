using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
public sealed record PolicyCreatedEvent(Guid PolicyRefId, int WorkspaceId, string Name) : RegistrationPolicyEvent(PolicyRefId, WorkspaceId);
