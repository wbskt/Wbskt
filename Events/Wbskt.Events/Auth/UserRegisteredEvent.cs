using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record UserRegisteredEvent(int UserId, Guid UserRefId, string Username, string Email)
    : BaseEvent, IUserContext;
