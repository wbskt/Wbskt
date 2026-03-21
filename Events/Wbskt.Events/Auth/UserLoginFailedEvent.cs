using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Warning)]
public sealed record UserLoginFailedEvent(int UserId, Guid UserRefId, string IpAddress, string Reason) : BaseEvent, IUserContext;