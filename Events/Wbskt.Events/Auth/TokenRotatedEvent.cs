using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record TokenRotatedEvent(int UserId, Guid UserRefId, string IpAddress) : BaseEvent, IUserContext;
