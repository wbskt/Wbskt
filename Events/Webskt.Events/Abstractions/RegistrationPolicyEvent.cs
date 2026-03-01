using Webskt.EventBus.Abstractions;

namespace Webskt.Events.Abstractions;

public abstract record RegistrationPolicyEvent(Guid PolicyRefId, int WorkspaceId) : BaseEvent;