using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record UserRegisteredEvent(int UserId, Guid UserRefId, string Username, string Email)
    : BaseEvent, IUserContext;
