using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
public sealed record PolicyCreatedEvent(Guid PolicyRefId, int WorkspaceId, string Name) : RegistrationPolicyEvent(PolicyRefId, WorkspaceId);
